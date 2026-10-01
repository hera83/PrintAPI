namespace api.Services.Ipp.Dtos;

public sealed class IppPrinterStatusDto
{
    /// <summary><c>idle</c>, <c>processing</c> or <c>stopped</c>.</summary>
    public string? State { get; set; }
    public List<string> StateReasons { get; set; } = [];
    public string? StateMessage { get; set; }
    public bool? IsAcceptingJobs { get; set; }
}
