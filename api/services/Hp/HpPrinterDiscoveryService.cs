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
    private const int MinScanPrefixLength = 16; // never scan more than ~65,000 hosts per interface (a /16 takes ~2 min)
    private const int MaxParallelPrinterQueries = 16;
    private const int MaxParallelPortProbes = 256;

    private static readonly string[] PrinterServiceTypes = ["_ipp._tcp.local", "_ipps._tcp.local", "_pdl-datastream._tcp.local", "_printer._tcp.local"];
    // Virtual bridges created by Docker/libvirt on the host (visible with network_mode: host); they never contain printers.
    private static readonly string[] SkippedInterfacePrefixes = ["docker", "br-", "veth", "virbr"];
    private static readonly string[] FallbackResourcePaths = ["ipp/print", "ipp", ""];
    private static readonly TimeSpan PrinterQueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PortProbeTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan InspectMdnsTimeout = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<DiscoveredHpPrinterDto>> DiscoverAsync(
        HpDiscoveryRequestDto request,
        HpDiscoveryProgress? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.SetPhase(HpDiscoveryProgress.PhaseMdns);
        var instances = await mdnsBrowser.BrowseAsync(PrinterServiceTypes, TimeSpan.FromSeconds(request.TimeoutSeconds), cancellationToken);
        var candidates = GroupByDevice(instances);

        if (request.ScanSubnet)
        {
            progress?.SetPhase(HpDiscoveryProgress.PhaseSubnetScan);
            var knownHosts = candidates.Select(c => c.Host).ToHashSet();
            foreach (var address in await ScanLocalSubnetsAsync(progress, cancellationToken))
            {
                var host = address.ToString();
                if (knownHosts.Add(host))
                {
                    candidates.Add(new Candidate(host, host, null, DefaultIppPort, null, null, [], [], "subnet-scan"));
                }
            }
        }

        progress?.SetPhase(HpDiscoveryProgress.PhaseInspecting);
        progress?.SetDevicesToInspect(candidates.Count);

        var results = new ConcurrentBag<DiscoveredHpPrinterDto>();
        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelPrinterQueries, CancellationToken = cancellationToken },
            async (candidate, token) =>
            {
                results.Add(await InspectCandidateAsync(candidate, token));
                progress?.DeviceInspected();
            });

        logger.LogInformation("Printer discovery found {Total} device(s), {HpCount} HP", results.Count, results.Count(r => r.IsHp));

        return results
            // A device that didn't answer the IPP query can't be identified as HP, but it may well be the printer
            // (e.g. one that's slow to wake up), so it's included with its InspectionError rather than hidden.
            .Where(r => request.IncludeNonHp || r.IsHp || r.InspectionError is not null)
            .OrderBy(r => r.PrinterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Host)
            .ToList();
    }

    public async Task<HpPrinterInfoDto> InspectAsync(
        string host,
        string? preferredResourcePath = null,
        CancellationToken cancellationToken = default)
    {
        host = host.Trim();
        var addresses = await ResolveAsync(host, cancellationToken);
        var mdnsTask = FindMdnsCandidateAsync(host, addresses, cancellationToken);

        const int port = DefaultIppPort;
        var resourcePaths = (preferredResourcePath is null ? FallbackResourcePaths : FallbackResourcePaths.Prepend(preferredResourcePath))
            .Select(HpPrinterInfoBuilder.NormalizeResourcePath)
            .Distinct()
            .ToList();

        Dictionary<string, List<string>> attributes;
        string resourcePath;
        try
        {
            (attributes, resourcePath) = await QueryAttributesAsync(host, port, resourcePaths, cancellationToken);
        }
        catch (IppException ex) when (!ex.IsUnreachable)
        {
            // None of the usual paths worked; the printer may advertise its own via mDNS (TXT "rp").
            var advertised = (await mdnsTask)?.ResourcePath is { } rp ? HpPrinterInfoBuilder.NormalizeResourcePath(rp) : null;
            if (advertised is null || resourcePaths.Contains(advertised))
            {
                throw;
            }

            (attributes, resourcePath) = await QueryAttributesAsync(host, port, [advertised], cancellationToken);
        }

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

    /// <summary>
    /// Tries each resource path until the printer answers Get-Printer-Attributes. Stops at once if the printer can't be
    /// reached at all, since another path won't help; otherwise rethrows the last path's error.
    /// </summary>
    private async Task<(Dictionary<string, List<string>> Attributes, string ResourcePath)> QueryAttributesAsync(
        string host,
        int port,
        IReadOnlyList<string> resourcePaths,
        CancellationToken cancellationToken)
    {
        IppException? lastError = null;
        foreach (var path in resourcePaths.Select(HpPrinterInfoBuilder.NormalizeResourcePath).Distinct())
        {
            try
            {
                var attributes = await ippClient.GetPrinterAttributesAsync(
                    HpPrinterInfoBuilder.BuildPrinterUri(host, port, path),
                    cancellationToken);
                return (attributes, path);
            }
            catch (IppException ex) when (!ex.IsUnreachable)
            {
                logger.LogDebug("IPP query of {Host} at path '{Path}' failed: {Error}", host, path, ex.Message);
                lastError = ex;
            }
        }

        throw lastError!;
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

    private async Task<IReadOnlyList<IPAddress>> ScanLocalSubnetsAsync(HpDiscoveryProgress? progress, CancellationToken cancellationToken)
    {
        var hosts = new HashSet<IPAddress>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || SkippedInterfacePrefixes.Any(p => networkInterface.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
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
                var hostCount = broadcast > network ? broadcast - network - 1 : 0;

                logger.LogInformation(
                    "Scanning {Address}/{PrefixLength} on {Interface} ({HostCount} host(s)) for IPP port {Port}",
                    unicastAddress.Address, unicastAddress.PrefixLength, networkInterface.Name, hostCount, DefaultIppPort);

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

        progress?.AddHostsToProbe(hosts.Count);

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
                finally
                {
                    progress?.HostProbed();
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
