namespace api.Dtos.Health;

public class HealthResponseDto
{
    /// <summary><c>Healthy</c> when at least one active printer is online, otherwise <c>Unhealthy</c>.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public int PrintersOnline { get; set; }
    public int PrintersTotal { get; set; }

    /// <summary>Every active registered printer and whether it's reachable right now.</summary>
    public List<PrinterHealthDto> Printers { get; set; } = [];
}
