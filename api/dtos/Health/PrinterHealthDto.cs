namespace api.Dtos.Health;

public class PrinterHealthDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>True when the printer answered an IPP status query within the health check's timeout.</summary>
    public bool IsOnline { get; set; }

    /// <summary><c>idle</c>, <c>processing</c> or <c>stopped</c>, as reported by the printer. Null when it's offline.</summary>
    public string? State { get; set; }
    public List<string> StateReasons { get; set; } = [];
    public bool? IsAcceptingJobs { get; set; }

    /// <summary>Why the printer counts as offline (timeout, unreachable, IPP error). Null when it's online.</summary>
    public string? Error { get; set; }
}
