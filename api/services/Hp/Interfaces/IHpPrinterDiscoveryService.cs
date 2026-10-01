using api.Dtos.Hp;

namespace api.Services.Hp.Interfaces;

public interface IHpPrinterDiscoveryService
{
    /// <summary>
    /// Finds printers on the local network(s) via mDNS (and optionally a subnet scan) and queries each one over IPP.
    /// Can take minutes with a subnet scan, so it's run by <c>HpDiscoveryWorker</c>, not inside an HTTP request.
    /// </summary>
    Task<IReadOnlyList<DiscoveredHpPrinterDto>> DiscoverAsync(HpDiscoveryRequestDto request, HpDiscoveryProgress? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads everything about the printer at <paramref name="host"/> (IP address or hostname) over IPP on port 631, finding
    /// its IPP resource path by itself (<paramref name="preferredResourcePath"/> first, if given), and enriches it with its
    /// mDNS TXT records if it advertises any. Throws <c>IppException</c> if no path answers.
    /// </summary>
    Task<HpPrinterInfoDto> InspectAsync(string host, string? preferredResourcePath = null, CancellationToken cancellationToken = default);
}
