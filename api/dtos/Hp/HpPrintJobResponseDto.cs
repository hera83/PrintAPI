namespace api.Dtos.Hp;

public class HpPrintJobResponseDto
{
    public int PrinterId { get; set; }
    public string PrinterName { get; set; } = string.Empty;

    /// <summary>Job id assigned by the printer.</summary>
    public int? JobId { get; set; }
    public string? JobUri { get; set; }

    /// <summary><c>pending</c>, <c>processing</c>, <c>completed</c>, ... as reported right after the job was accepted.</summary>
    public string? JobState { get; set; }
    public List<string> JobStateReasons { get; set; } = [];

    public string IppStatusCode { get; set; } = string.Empty;
    public string? IppStatusMessage { get; set; }
}
