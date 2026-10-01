namespace api.Data.Models;

public class HpPrinter
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    public string Host { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? MdnsHostName { get; set; }
    public int Port { get; set; } = 631;
    public string ResourcePath { get; set; } = string.Empty;
    public string PrinterUri { get; set; } = string.Empty;

    public string? PrinterName { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? MakeAndModel { get; set; }
    public string? SerialNumber { get; set; }
    public string? Uuid { get; set; }
    public string? DeviceId { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? Location { get; set; }
    public string? Info { get; set; }
    public string? AdminUrl { get; set; }

    public string? State { get; set; }
    public List<string> StateReasons { get; set; } = [];
    public string? StateMessage { get; set; }

    public bool? SupportsColor { get; set; }
    public bool? SupportsDuplex { get; set; }
    public List<string> DocumentFormats { get; set; } = [];
    public List<string> MediaSupported { get; set; } = [];
    public string? MediaDefault { get; set; }
    public List<string> Resolutions { get; set; } = [];
    public List<string> IppVersions { get; set; } = [];
    public List<string> MdnsServices { get; set; } = [];

    /// <summary>Raw IPP Get-Printer-Attributes result as JSON (<c>Dictionary&lt;string, List&lt;string&gt;&gt;</c>).</summary>
    public string IppAttributesJson { get; set; } = "{}";

    /// <summary>Raw mDNS TXT records as JSON (<c>Dictionary&lt;string, string&gt;</c>).</summary>
    public string MdnsTxtRecordsJson { get; set; } = "{}";

    public DateTime InfoRetrievedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
