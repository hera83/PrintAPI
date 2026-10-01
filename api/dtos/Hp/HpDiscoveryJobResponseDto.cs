namespace api.Dtos.Hp;

public class HpDiscoveryJobResponseDto
{
    public Guid Id { get; set; }

    /// <summary><c>Queued</c>, <c>Running</c>, <c>Completed</c> or <c>Failed</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>True once <see cref="Printers"/> (or <see cref="Error"/>) is final.</summary>
    public bool IsFinished { get; set; }

    /// <summary>What the discovery is doing right now: <c>mdns</c>, <c>subnet-scan</c> or <c>inspecting</c>.</summary>
    public string? Phase { get; set; }

    /// <summary>Subnet scan: addresses to probe for IPP port 631, and how many have been probed so far.</summary>
    public int HostsToProbe { get; set; }
    public int HostsProbed { get; set; }

    /// <summary>Devices found (via mDNS or the subnet scan) that are being queried over IPP, and how many are done.</summary>
    public int DevicesToInspect { get; set; }
    public int DevicesInspected { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Why the discovery failed, if it did.</summary>
    public string? Error { get; set; }

    /// <summary>The printers found; filled in when <see cref="Status"/> is <c>Completed</c>.</summary>
    public List<DiscoveredHpPrinterDto> Printers { get; set; } = [];
}
