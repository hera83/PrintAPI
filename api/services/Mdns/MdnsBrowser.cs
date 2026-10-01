using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using api.Services.Mdns.Dtos;
using api.Services.Mdns.Interfaces;

namespace api.Services.Mdns;

/// <summary>
/// One-shot mDNS/DNS-SD browser (RFC 6762/6763). Queries are sent from an ephemeral port on every IPv4
/// interface ("legacy unicast", RFC 6762 §6.7), so responders answer straight back to us and we never need
/// to bind port 5353 — which on Windows is usually already taken by the OS' own mDNS responder.
/// </summary>
public class MdnsBrowser(ILogger<MdnsBrowser> logger) : IMdnsBrowser
{
    private const ushort TypeA = 1;
    private const ushort TypePtr = 12;
    private const ushort TypeTxt = 16;
    private const ushort TypeSrv = 33;
    private const ushort ClassIn = 1;

    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("224.0.0.251"), 5353);

    public async Task<IReadOnlyList<MdnsServiceInstanceDto>> BrowseAsync(
        IReadOnlyCollection<string> serviceTypes,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var normalizedTypes = serviceTypes.Select(NormalizeServiceType).ToList();
        var store = new RecordStore();
        var clients = CreateClients();

        try
        {
            if (clients.Count == 0)
            {
                logger.LogWarning("mDNS browse skipped: no multicast-capable IPv4 network interface is up");
                return [];
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var receiveTasks = clients.Select(client => ReceiveLoopAsync(client, store, timeoutCts.Token)).ToList();

            await SendToAllAsync(clients, BuildQuery(normalizedTypes.Select(type => (type, TypePtr))), timeoutCts.Token);

            // Responders normally include SRV/TXT/A as additional records; halfway through, explicitly ask for whatever is still missing.
            try
            {
                await Task.Delay(timeout / 2, timeoutCts.Token);
                var missing = store.GetMissingQuestions();
                if (missing.Count > 0)
                {
                    await SendToAllAsync(clients, BuildQuery(missing), timeoutCts.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.WhenAll(receiveTasks);
            cancellationToken.ThrowIfCancellationRequested();

            return store.BuildInstances(normalizedTypes);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    private List<UdpClient> CreateClients()
    {
        var clients = new List<UdpClient>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || !networkInterface.SupportsMulticast
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                try
                {
                    var client = new UdpClient(new IPEndPoint(unicastAddress.Address, 0));
                    client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, unicastAddress.Address.GetAddressBytes());
                    client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                    clients.Add(client);
                }
                catch (SocketException ex)
                {
                    logger.LogDebug(ex, "Could not open mDNS socket on {Address}", unicastAddress.Address);
                }
            }
        }

        return clients;
    }

    private async Task SendToAllAsync(List<UdpClient> clients, byte[] packet, CancellationToken cancellationToken)
    {
        foreach (var client in clients)
        {
            try
            {
                await client.SendAsync(packet, MulticastEndpoint, cancellationToken);
            }
            catch (SocketException ex)
            {
                logger.LogDebug(ex, "Could not send mDNS query from {Endpoint}", client.Client.LocalEndPoint);
            }
        }
    }

    private async Task ReceiveLoopAsync(UdpClient client, RecordStore store, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException ex)
            {
                // Windows surfaces ICMP port-unreachable as ConnectionReset on UDP sockets; just keep listening.
                logger.LogDebug(ex, "mDNS receive error on {Endpoint}", client.Client.LocalEndPoint);
                continue;
            }

            try
            {
                ParsePacket(result.Buffer, result.RemoteEndPoint.Address, store);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException or InvalidDataException)
            {
                logger.LogDebug(ex, "Ignoring malformed mDNS packet from {Address}", result.RemoteEndPoint.Address);
            }
        }
    }

    private static byte[] BuildQuery(IEnumerable<(string Name, ushort Type)> questions)
    {
        var questionList = questions.ToList();

        using var stream = new MemoryStream();
        WriteUInt16(stream, (ushort)Random.Shared.Next(1, ushort.MaxValue)); // id (echoed back in legacy unicast replies)
        WriteUInt16(stream, 0); // flags: standard query
        WriteUInt16(stream, (ushort)questionList.Count);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);

        foreach (var (name, type) in questionList)
        {
            foreach (var label in SplitLabels(name))
            {
                var bytes = Encoding.UTF8.GetBytes(label);
                stream.WriteByte((byte)bytes.Length);
                stream.Write(bytes);
            }

            stream.WriteByte(0);
            WriteUInt16(stream, type);
            WriteUInt16(stream, ClassIn);
        }

        return stream.ToArray();
    }

    private static void ParsePacket(byte[] data, IPAddress source, RecordStore store)
    {
        if (data.Length < 12)
        {
            return;
        }

        var flags = ReadUInt16(data, 2);
        if ((flags & 0x8000) == 0)
        {
            return; // a query, not a response
        }

        var questionCount = ReadUInt16(data, 4);
        var recordCount = ReadUInt16(data, 6) + ReadUInt16(data, 8) + ReadUInt16(data, 10);
        var position = 12;

        for (var i = 0; i < questionCount; i++)
        {
            ReadName(data, ref position);
            position += 4; // type + class
        }

        for (var i = 0; i < recordCount; i++)
        {
            var name = ReadName(data, ref position);
            var type = ReadUInt16(data, position);
            var length = ReadUInt16(data, position + 8);
            var rdata = position + 10;
            if (rdata + length > data.Length)
            {
                throw new InvalidDataException("Record data exceeds packet length.");
            }

            switch (type)
            {
                case TypePtr:
                    var ptrPosition = rdata;
                    store.AddPointer(name, ReadName(data, ref ptrPosition));
                    break;
                case TypeSrv:
                    var targetPosition = rdata + 6;
                    store.AddService(name, ReadName(data, ref targetPosition), ReadUInt16(data, rdata + 4), source);
                    break;
                case TypeTxt:
                    store.AddText(name, ReadTxt(data, rdata, length), source);
                    break;
                case TypeA when length == 4:
                    store.AddAddress(name, new IPAddress(data.AsSpan(rdata, 4)));
                    break;
            }

            position = rdata + length;
        }
    }

    /// <summary>Reads a (possibly compressed) DNS name; literal dots inside a label are escaped as <c>\.</c>.</summary>
    private static string ReadName(byte[] data, ref int position)
    {
        var labels = new List<string>();
        var cursor = position;
        var jumped = false;
        var jumps = 0;

        while (true)
        {
            var length = data[cursor];
            if (length == 0)
            {
                cursor++;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (++jumps > 32)
                {
                    throw new InvalidDataException("Too many DNS name compression pointers.");
                }

                if (!jumped)
                {
                    position = cursor + 2;
                    jumped = true;
                }

                cursor = ((length & 0x3F) << 8) | data[cursor + 1];
                continue;
            }

            var label = Encoding.UTF8.GetString(data, cursor + 1, length);
            labels.Add(label.Replace("\\", "\\\\").Replace(".", "\\."));
            cursor += 1 + length;
        }

        if (!jumped)
        {
            position = cursor;
        }

        return string.Join('.', labels);
    }

    private static Dictionary<string, string> ReadTxt(byte[] data, int start, int length)
    {
        var records = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cursor = start;
        var end = start + length;

        while (cursor < end)
        {
            var entryLength = data[cursor++];
            if (cursor + entryLength > end)
            {
                break;
            }

            var entry = Encoding.UTF8.GetString(data, cursor, entryLength);
            cursor += entryLength;

            var separator = entry.IndexOf('=');
            var key = separator < 0 ? entry : entry[..separator];
            if (key.Length > 0)
            {
                records.TryAdd(key, separator < 0 ? string.Empty : entry[(separator + 1)..]);
            }
        }

        return records;
    }

    private static List<string> SplitLabels(string name)
    {
        var labels = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '\\' && i + 1 < name.Length)
            {
                current.Append(name[++i]);
            }
            else if (c == '.')
            {
                if (current.Length > 0)
                {
                    labels.Add(current.ToString());
                }

                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            labels.Add(current.ToString());
        }

        return labels;
    }

    private static string NormalizeServiceType(string serviceType)
    {
        var trimmed = serviceType.Trim().TrimEnd('.');
        return trimmed.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".local";
    }

    private static ushort ReadUInt16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    /// <summary>Thread-safe accumulator for records received across all interface sockets.</summary>
    private sealed class RecordStore
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<string, HashSet<string>> _pointers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string Target, int Port)> _services = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _texts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<IPAddress>> _addresses = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IPAddress> _responders = new(StringComparer.OrdinalIgnoreCase);

        public void AddPointer(string serviceType, string instanceName)
        {
            lock (_lock)
            {
                if (!_pointers.TryGetValue(serviceType, out var instances))
                {
                    instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _pointers[serviceType] = instances;
                }

                instances.Add(instanceName);
            }
        }

        public void AddService(string instanceName, string target, int port, IPAddress responder)
        {
            lock (_lock)
            {
                _services[instanceName] = (target, port);
                _responders.TryAdd(instanceName, responder);
            }
        }

        public void AddText(string instanceName, Dictionary<string, string> records, IPAddress responder)
        {
            lock (_lock)
            {
                _texts[instanceName] = records;
                _responders.TryAdd(instanceName, responder);
            }
        }

        public void AddAddress(string hostName, IPAddress address)
        {
            lock (_lock)
            {
                if (!_addresses.TryGetValue(hostName, out var addresses))
                {
                    addresses = [];
                    _addresses[hostName] = addresses;
                }

                addresses.Add(address);
            }
        }

        public List<(string Name, ushort Type)> GetMissingQuestions()
        {
            lock (_lock)
            {
                var questions = new List<(string Name, ushort Type)>();

                foreach (var instanceName in _pointers.Values.SelectMany(i => i).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!_services.ContainsKey(instanceName))
                    {
                        questions.Add((instanceName, TypeSrv));
                    }

                    if (!_texts.ContainsKey(instanceName))
                    {
                        questions.Add((instanceName, TypeTxt));
                    }
                }

                foreach (var target in _services.Values.Select(s => s.Target).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!_addresses.ContainsKey(target))
                    {
                        questions.Add((target, TypeA));
                    }
                }

                return questions;
            }
        }

        public List<MdnsServiceInstanceDto> BuildInstances(IReadOnlyCollection<string> serviceTypes)
        {
            lock (_lock)
            {
                var instances = new List<MdnsServiceInstanceDto>();

                foreach (var serviceType in serviceTypes)
                {
                    if (!_pointers.TryGetValue(serviceType, out var instanceNames))
                    {
                        continue;
                    }

                    foreach (var instanceName in instanceNames)
                    {
                        var hasService = _services.TryGetValue(instanceName, out var service);
                        List<IPAddress> addresses = hasService && _addresses.TryGetValue(service.Target, out var resolved)
                            ? resolved.ToList()
                            : _responders.TryGetValue(instanceName, out var responder) ? [responder] : [];

                        instances.Add(new MdnsServiceInstanceDto
                        {
                            InstanceName = instanceName,
                            ServiceType = serviceType,
                            DisplayName = SplitLabels(instanceName).FirstOrDefault() ?? instanceName,
                            HostName = hasService ? service.Target : null,
                            Port = hasService ? service.Port : 0,
                            Addresses = addresses,
                            TxtRecords = _texts.GetValueOrDefault(instanceName) ?? new(StringComparer.OrdinalIgnoreCase)
                        });
                    }
                }

                return instances;
            }
        }
    }
}
