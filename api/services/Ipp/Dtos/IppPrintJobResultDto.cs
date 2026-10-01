namespace api.Services.Ipp.Dtos;

public sealed class IppPrintJobResultDto
{
    public int? JobId { get; set; }
    public string? JobUri { get; set; }

    /// <summary><c>pending</c>, <c>pending-held</c>, <c>processing</c>, <c>processing-stopped</c>, <c>canceled</c>, <c>aborted</c> or <c>completed</c>.</summary>
    public string? JobState { get; set; }
    public List<string> JobStateReasons { get; set; } = [];

    /// <summary>IPP status code, e.g. <c>0x0000</c> (successful-ok) or <c>0x0001</c> (some attributes were ignored/substituted).</summary>
    public string IppStatusCode { get; set; } = string.Empty;
    public string? IppStatusMessage { get; set; }
}
