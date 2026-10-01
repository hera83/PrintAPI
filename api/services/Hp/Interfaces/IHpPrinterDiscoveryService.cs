using api.Dtos.Hp;

namespace api.Services.Hp.Interfaces;

public interface IHpPrinterDiscoveryService
{
    /// <summary>Finds printers on the local network(s) via mDNS (and optionally a subnet scan) and queries each one over IPP.</summary>
    Task<IReadOnlyList<DiscoveredHpPrinterDto>> DiscoverAsync(HpDiscoveryRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Queries one printer over IPP (throws <c>IppException</c> if unreachable) and enriches it with its mDNS TXT records if it advertises any.</summary>
    Task<HpPrinterInfoDto> InspectAsync(string host, int port, string resourcePath, CancellationToken cancellationToken = default);
}
