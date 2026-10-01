using System.Net;

namespace api.Services.Mdns.Dtos;

public sealed class MdnsServiceInstanceDto
{
    /// <summary>Full DNS-SD instance name, e.g. <c>HP LaserJet M404 [A1B2C3]._ipp._tcp.local</c> (dots inside the instance label are escaped as <c>\.</c>).</summary>
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>Service type the instance was found under, e.g. <c>_ipp._tcp.local</c>.</summary>
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>Human-readable instance label, e.g. <c>HP LaserJet M404 [A1B2C3]</c>.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>SRV target host, e.g. <c>HPA1B2C3.local</c>.</summary>
    public string? HostName { get; set; }

    public int Port { get; set; }

    public List<IPAddress> Addresses { get; set; } = [];

    public Dictionary<string, string> TxtRecords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
