using System.ComponentModel.DataAnnotations;

namespace api.Dtos.Hp;

public class HpDiscoveryRequestDto
{
    /// <summary>How long to listen for mDNS answers.</summary>
    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 3;

    /// <summary>Also probe every host on the local subnet(s) (/22 or smaller) for IPP port 631 — finds printers that don't advertise via mDNS, but is slower.</summary>
    public bool ScanSubnet { get; set; }

    /// <summary>Include printers that aren't HP.</summary>
    public bool IncludeNonHp { get; set; }
}
