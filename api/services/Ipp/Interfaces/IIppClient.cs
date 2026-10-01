using api.Services.Ipp.Dtos;

namespace api.Services.Ipp.Interfaces;

public interface IIppClient
{
    /// <summary>
    /// Sends an IPP Get-Printer-Attributes request (requested-attributes = all) to <paramref name="printerUri"/>
    /// (e.g. <c>ipp://192.168.1.20:631/ipp/print</c>) and returns every printer attribute, each value rendered as a string.
    /// </summary>
    Task<Dictionary<string, List<string>>> GetPrinterAttributesAsync(Uri printerUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the printer only for its state (Get-Printer-Attributes with a short requested-attributes list) — a cheap
    /// "is it there and how is it doing" check, much lighter than <see cref="GetPrinterAttributesAsync"/>.
    /// </summary>
    Task<IppPrinterStatusDto> GetPrinterStatusAsync(Uri printerUri, CancellationToken cancellationToken = default);

    /// <summary>Sends a document to the printer with an IPP Print-Job request and returns the job the printer created.</summary>
    Task<IppPrintJobResultDto> PrintJobAsync(Uri printerUri, IppPrintJobRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Asks the printer for the current state of a job it created (IPP Get-Job-Attributes).</summary>
    Task<IppJobStatusDto> GetJobAttributesAsync(Uri printerUri, int jobId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a job on the printer (IPP Cancel-Job).</summary>
    Task CancelJobAsync(Uri printerUri, int jobId, CancellationToken cancellationToken = default);
}
