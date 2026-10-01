using api.Services.Mdns.Dtos;

namespace api.Services.Mdns.Interfaces;

public interface IMdnsBrowser
{
    /// <summary>
    /// Browses the local network(s) via multicast DNS / DNS-SD for the given service types
    /// (e.g. <c>_ipp._tcp.local</c>) and returns every instance that answered within <paramref name="timeout"/>.
    /// </summary>
    Task<IReadOnlyList<MdnsServiceInstanceDto>> BrowseAsync(
        IReadOnlyCollection<string> serviceTypes,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
