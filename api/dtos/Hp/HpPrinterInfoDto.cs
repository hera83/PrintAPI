namespace api.Dtos.Hp;

/// <summary>Everything known about a printer — normalized fields plus the raw IPP attributes and mDNS TXT records they came from.</summary>
public class HpPrinterInfoDto
{
    /// <summary>Host (IP address or hostname) the API uses to reach the printer.</summary>
    public string Host { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    /// <summary>The printer's own mDNS hostname, e.g. <c>HPA1B2C3.local</c>.</summary>
    public string? MdnsHostName { get; set; }
    public int Port { get; set; }
    /// <summary>IPP resource path, e.g. <c>ipp/print</c>.</summary>
    public string ResourcePath { get; set; } = string.Empty;
    /// <summary>Full IPP URI, e.g. <c>ipp://192.168.1.20:631/ipp/print</c>.</summary>
    public string PrinterUri { get; set; } = string.Empty;

    public string? PrinterName { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? MakeAndModel { get; set; }
    public string? SerialNumber { get; set; }
    public string? Uuid { get; set; }
    /// <summary>IEEE 1284 device ID string (MFG/MDL/CMD/...).</summary>
    public string? DeviceId { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? Location { get; set; }
    public string? Info { get; set; }
    /// <summary>The printer's embedded web server (EWS) admin page.</summary>
    public string? AdminUrl { get; set; }

    /// <summary><c>idle</c>, <c>processing</c> or <c>stopped</c> at the time the info was retrieved.</summary>
    public string? State { get; set; }
    public List<string> StateReasons { get; set; } = [];
    public string? StateMessage { get; set; }

    public bool? SupportsColor { get; set; }
    public bool? SupportsDuplex { get; set; }
    /// <summary>MIME types the printer accepts, e.g. <c>application/pdf</c>, <c>image/urf</c>.</summary>
    public List<string> DocumentFormats { get; set; } = [];
    public List<string> MediaSupported { get; set; } = [];
    public string? MediaDefault { get; set; }
    public List<string> Resolutions { get; set; } = [];
    public List<string> IppVersions { get; set; } = [];

    /// <summary>Ink/toner/drum levels at the time the info was retrieved.</summary>
    public List<HpPrinterSupplyDto> Supplies { get; set; } = [];

    /// <summary>DNS-SD service types the printer advertised, e.g. <c>_ipp._tcp.local</c>.</summary>
    public List<string> MdnsServices { get; set; } = [];

    public bool IsHp { get; set; }

    /// <summary>Every attribute returned by IPP Get-Printer-Attributes (requested-attributes = all), values rendered as strings.</summary>
    public Dictionary<string, List<string>> IppAttributes { get; set; } = [];

    /// <summary>Every mDNS TXT record key/value the printer advertised.</summary>
    public Dictionary<string, string> MdnsTxtRecords { get; set; } = [];

    /// <summary>UTC time the information above was retrieved from the printer.</summary>
    public DateTime InfoRetrievedAt { get; set; }
}
