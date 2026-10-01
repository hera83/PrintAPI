namespace api.Services.Ipp.Dtos;

public sealed class IppJobStatusDto
{
    /// <summary><c>pending</c>, <c>pending-held</c>, <c>processing</c>, <c>processing-stopped</c>, <c>canceled</c>, <c>aborted</c> or <c>completed</c>.</summary>
    public string? JobState { get; set; }
    public List<string> JobStateReasons { get; set; } = [];
    public string? JobStateMessage { get; set; }

    /// <summary>Pages (impressions) printed so far, if the printer reports it.</summary>
    public int? ImpressionsCompleted { get; set; }
}
