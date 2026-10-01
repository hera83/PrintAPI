namespace api.Dtos.Hp;

public class DiscoveredHpPrinterDto : HpPrinterInfoDto
{
    /// <summary><c>mdns</c> or <c>subnet-scan</c>.</summary>
    public string DiscoveredVia { get; set; } = string.Empty;

    /// <summary>Id of the matching registered printer, or null if it isn't registered yet.</summary>
    public int? RegisteredPrinterId { get; set; }

    /// <summary>Why the IPP query failed, if it did (the printer is then described from mDNS data only).</summary>
    public string? InspectionError { get; set; }
}
