using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using api.Dtos.Hp;
using api.Services.Hp.Interfaces;
using api.Services.Ipp;
using api.Services.Ipp.Interfaces;
using api.Services.Mdns.Dtos;
using api.Services.Mdns.Interfaces;

namespace api.Services.Hp;

public class HpPrinterDiscoveryService(
    IMdnsBrowser mdnsBrowser,
    IIppClient ippClient,
    ILogger<HpPrinterDiscoveryService> logger) : IHpPrinterDiscoveryService
{
    private const int DefaultIppPort = 631;
    private const int MinScanPrefixLength = 22; // never scan more than ~1000 hosts per interface
    private const int MaxParallelPrinterQueries = 16;
    private const int MaxParallelPortProbes = 128;

    private static readonly string[] PrinterServiceTypes = ["_ipp._tcp.local", "_ipps._tcp.local", "_pdl-datastream._tcp.local", "_printer._tcp.local"];
    private static readonly string[] FallbackResourcePaths = ["ipp/print", "ipp", ""];
    private static readonly TimeSpan PrinterQueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PortProbeTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan InspectMdnsTimeout = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<DiscoveredHpPrinterDto>> DiscoverAsync(
        HpDiscoveryRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var instances = await mdnsBrowser.BrowseAsync(PrinterServiceTypes, TimeSpan.FromSeconds(request.TimeoutSeconds), cancellationToken);
        var candidates = GroupByDevice(instances);

        if (request.ScanSubnet)
        {
            var knownHosts = candidates.Select(c => c.Host).ToHashSet();
            foreach (var address in await ScanLocalSubnetsAsync(cancellationToken))
            {
                var host = address.ToString();
                if (knownHosts.Add(host))
                {
                    candidates.Add(new Candidate(host, host, null, DefaultIppPort, null, null, [], [], "subnet-scan"));
                }
            }
        }

        var results = new ConcurrentBag<DiscoveredHpPrinterDto>();
        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelPrinterQueries, CancellationToken = cancellationToken },
            async (candidate, token) => results.Add(await InspectCandidateAsync(candidate, token)));

        logger.LogInformation("Printer discovery found {Total} device(s), {HpCount} HP", results.Count, results.Count(r => r.IsHp));

        return results
            .Where(r => request.IncludeNonHp || r.IsHp)
            .OrderBy(r => r.PrinterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Host)
            .ToList();
    }

    public async Task<HpPrinterInfoDto> InspectAsync(
        string host,
        int port,
        string resourcePath,
        CancellationToken cancellationToken = default)
    {
        host = host.Trim();
        var addresses = await ResolveAsync(host, cancellationToken);
        var mdnsTask = FindMdnsCandidateAsync(host, addresses, cancellationToken);

        var attributes = await ippClient.GetPrinterAttributesAsync(
            HpPrinterInfoBuilder.BuildPrinterUri(host, port, resourcePath),
            cancellationToken);
        var mdns = await mdnsTask;

        var info = new HpPrinterInfoDto();
        HpPrinterInfoBuilder.Populate(
            info,
            host,
            addresses.FirstOrDefault()?.ToString(),
            port,
            resourcePath,
            attributes,
            mdns?.TxtRecords ?? [],
            mdns?.MdnsServices ?? [],
            mdns?.MdnsHostName,
            mdns?.DisplayName);
        return info;
    }

    private async Task<DiscoveredHpPrinterDto> InspectCandidateAsync(Candidate candidate, CancellationToken cancellationToken)
    {
        Dictionary<string, List<string>>? attributes = null;
        var resourcePath = candidate.ResourcePath ?? FallbackResourcePaths[0];
        string? error = null;

        var pathsToTry = candidate.ResourcePath is null ? FallbackResourcePaths : new[] { candidate.ResourcePath };
        foreach (var path in pathsToTry)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(PrinterQueryTimeout);

            try
            {
                attributes = await ippClient.GetPrinterAttributesAsync(
                    HpPrinterInfoBuilder.BuildPrinterUri(candidate.Host, candidate.Port, path),
                    timeoutCts.Token);
                resourcePath = path;
                error = null;
                break;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                error = $"Timed out after {PrinterQueryTimeout.TotalSeconds:0}s waiting for an IPP response.";
            }
            catch (IppException ex)
            {
                error = ex.Message;
            }
        }

        if (error is not null)
        {
            logger.LogDebug("IPP query of discovered device {Host} failed: {Error}", candidate.Host, error);
        }

        var result = new DiscoveredHpPrinterDto { DiscoveredVia = candidate.DiscoveredVia, InspectionError = error };
        HpPrinterInfoBuilder.Populate(
            result,
            candidate.Host,
            candidate.IpAddress,
            candidate.Port,
            resourcePath,
            attributes,
            candidate.TxtRecords,
            candidate.MdnsServices,
            candidate.MdnsHostName,
            candidate.DisplayName);
        return result;
    }

    /// <summary>A printer usually advertises several services (_ipp, _ipps, _pdl-datastream, ...); merge them into one candidate per IP.</summary>
    private static List<Candidate> GroupByDevice(IReadOnlyList<MdnsServiceInstanceDto> instances)
    {
        var candidates = new List<Candidate>();

        foreach (var group in instances.Where(i => i.Addresses.Count > 0).GroupBy(i => i.Addresses[0].ToString()))
        {
            var ipp = group.FirstOrDefault(i => i.ServiceType.StartsWith("_ipp._tcp", StringComparison.OrdinalIgnoreCase));
            var primary = ipp ?? group.First();

            var txtRecords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var instance in group.OrderBy(i => i == ipp ? 0 : 1))
            {
                foreach (var (key, value) in instance.TxtRecords)
                {
                    txtRecords.TryAdd(key, value); // _ipp._tcp's TXT wins on conflicts
                }
            }

            candidates.Add(new Candidate(
                Host: group.Key,
                IpAddress: group.Key,
                MdnsHostName: primary.HostName,
                Port: ipp is { Port: > 0 } ? ipp.Port : DefaultIppPort,
                ResourcePath: ipp is not null && ipp.TxtRecords.TryGetValue("rp", out var rp) ? rp : null,
                DisplayName: primary.DisplayName,
                TxtRecords: txtRecords,
                MdnsServices: group.Select(i => i.ServiceType).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                DiscoveredVia: "mdns"));
        }

        return candidates;
    }

    private async Task<Candidate?> FindMdnsCandidateAsync(string host, IPAddress[] addresses, CancellationToken cancellationToken)
    {
        try
        {
            var targets = addresses.Select(a => a.ToString()).ToHashSet();
            var instances = await mdnsBrowser.BrowseAsync(PrinterServiceTypes, InspectMdnsTimeout, cancellationToken);
            return GroupByDevice(instances).FirstOrDefault(c =>
                targets.Contains(c.Host) || string.Equals(c.MdnsHostName, host.TrimEnd('.'), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "mDNS lookup for {Host} failed; continuing with IPP data only", host);
            return null;
        }
    }

    private async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return [address];
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
        }
        catch (SocketException ex)
        {
            logger.LogDebug(ex, "Could not resolve {Host}", host);
            return [];
        }
    }

    private async Task<IReadOnlyList<IPAddress>> ScanLocalSubnetsAsync(CancellationToken cancellationToken)
    {
        var hosts = new HashSet<IPAddress>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
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

                if (unicastAddress.PrefixLength < MinScanPrefixLength)
                {
                    logger.LogInformation(
                        "Skipping subnet scan of {Address}/{PrefixLength}: larger than /{MinPrefixLength}",
                        unicastAddress.Address, unicastAddress.PrefixLength, MinScanPrefixLength);
                    continue;
                }

                var self = BinaryPrimitives.ReadUInt32BigEndian(unicastAddress.Address.GetAddressBytes());
                var mask = uint.MaxValue << (32 - unicastAddress.PrefixLength);
                var network = self & mask;
                var broadcast = network | ~mask;

                for (var host = network + 1; host < broadcast; host++)
                {
                    if (host != self)
                    {
                        var bytes = new byte[4];
                        BinaryPrimitives.WriteUInt32BigEndian(bytes, host);
                        hosts.Add(new IPAddress(bytes));
                    }
                }
            }
        }

        var found = new ConcurrentBag<IPAddress>();
        await Parallel.ForEachAsync(
            hosts,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelPortProbes, CancellationToken = cancellationToken },
            async (address, token) =>
            {
                using var client = new TcpClient();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(PortProbeTimeout);

                try
                {
                    await client.ConnectAsync(address, DefaultIppPort, timeoutCts.Token);
                    found.Add(address);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                }
                catch (SocketException)
                {
                }
            });

        logger.LogInformation("Subnet scan probed {HostCount} host(s), {OpenCount} with IPP port {Port} open", hosts.Count, found.Count, DefaultIppPort);
        return found.ToList();
    }

    private sealed record Candidate(
        string Host,
        string? IpAddress,
        string? MdnsHostName,
        int Port,
        string? ResourcePath,
        string? DisplayName,
        Dictionary<string, string> TxtRecords,
        List<string> MdnsServices,
        string DiscoveredVia);
}
