using System.Security.Claims;
using api.Data;
using api.Data.Models;
using api.Dtos.Print;
using api.Services.Authentication;
using api.Services.Ipp.Interfaces;
using api.Services.Pdf;
using api.Services.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

/// <summary>
/// Print queue for PDF documents. Jobs are stored and printed by <c>PrintQueueWorker</c> in the background; poll
/// <c>GetStatus</c> to follow a job until it is Completed, Failed or Canceled. Standard API keys only see their own
/// jobs; the master key sees every job. This is the only controller standard API keys can use.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AnyApiKey)]
[Route("[controller]/[action]")]
public class PrintController(
    ApiDbContext dbContext,
    PrintJobFileStore fileStore,
    PrintQueueSignal queueSignal,
    IIppClient ippClient) : ControllerBase
{
    // Base64 inflates by a third, so this allows PDFs of roughly 75 MB.
    private const long MaxRequestBytes = 100L * 1024 * 1024;

    /// <summary>Queues a base64-encoded PDF for printing and returns the job (202 Accepted) — follow it with GetStatus.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<ActionResult<PrintJobResponseDto>> Submit(
        SubmitPrintJobRequestDto request,
        CancellationToken cancellationToken)
    {
        var printer = await dbContext.HpPrinters.FindAsync([request.PrinterId!.Value], cancellationToken);
        if (printer is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: $"No printer is registered with id {request.PrinterId}.");
        }

        if (!printer.IsActive)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: $"Printer '{printer.Name}' is deactivated.");
        }

        if (!PrintJobProcessor.CanPrintPdf(printer))
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: $"Printer '{printer.Name}' accepts neither PDF nor PWG Raster (it supports {string.Join(", ", printer.DocumentFormats)}).");
        }

        if (request.Duplex && printer.SupportsDuplex == false)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, detail: $"Printer '{printer.Name}' can't print two-sided.");
        }

        if (!TryDecodeBase64(request.DocumentBase64, out var document, out var declaredMediaType))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "documentBase64 is not valid base64.");
        }

        if (declaredMediaType is not null && !declaredMediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: $"Only PDF files can be printed, but documentBase64 is declared as '{declaredMediaType}'.");
        }

        // Every PDF starts with "%PDF-" (the spec allows it within the first 1024 bytes).
        if (document.AsSpan(0, Math.Min(document.Length, 1024)).IndexOf("%PDF-"u8) < 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Only PDF files can be printed, and documentBase64 is not a PDF.");
        }

        if (!PdfDocument.TryOpen(document, out var pdf, out var pdfError))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: $"The PDF can't be printed: {pdfError}");
        }

        int pageCount;
        using (pdf)
        {
            pageCount = pdf.PageCount;
        }

        if (!PageRanges.TryParse(request.Pages, pageCount, out var pageRanges, out var pagesError))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: $"pages: {pagesError}");
        }

        var job = new PrintJob
        {
            PrinterId = printer.Id,
            Printer = printer,
            DocumentSizeBytes = document.Length,
            PageCount = pageCount,
            Pages = PageRanges.Format(pageRanges, pageCount),
            Copies = request.Copies,
            Color = request.Color,
            Duplex = request.Duplex,
            SubmittedByKeyId = CurrentKeyId,
            SubmittedBy = User.Identity?.Name ?? "unknown"
        };

        await fileStore.SaveDocumentAsync(job.Id, document, cancellationToken);
        dbContext.PrintJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        queueSignal.Notify();

        return AcceptedAtAction(nameof(GetStatus), new { id = job.Id }, ToResponseDto(job));
    }

    /// <summary>Current status of a print job.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PrintJobResponseDto>> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var job = await VisibleJobs()
            .Include(j => j.Printer)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        return Ok(ToResponseDto(job));
    }

    /// <summary>Lists print jobs, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<PrintJobSearchResponseDto>> GetAll(
        [FromQuery] PrintJobSearchRequestDto request,
        CancellationToken cancellationToken)
    {
        var query = VisibleJobs();
        if (request.PrinterId.HasValue)
        {
            query = query.Where(j => j.PrinterId == request.PrinterId.Value);
        }

        if (request.Status is not null)
        {
            var status = Enum.Parse<PrintJobStatus>(request.Status);
            query = query.Where(j => j.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var jobs = await query
            .Include(j => j.Printer)
            .OrderByDescending(j => j.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PrintJobSearchResponseDto
        {
            Items = jobs.Select(ToResponseDto).ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        });
    }

    /// <summary>Cancels a job that is still queued, or asks the printer to cancel one it is printing.</summary>
    [HttpPost("{id:guid}")]
    public async Task<ActionResult<PrintJobResponseDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var job = await VisibleJobs()
            .Include(j => j.Printer)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        switch (job.Status)
        {
            case PrintJobStatus.Processing:
                return Problem(statusCode: StatusCodes.Status409Conflict, detail: "The job is being sent to the printer right now; try again in a moment.");
            case PrintJobStatus.Completed or PrintJobStatus.Failed or PrintJobStatus.Canceled:
                return Problem(statusCode: StatusCodes.Status409Conflict, detail: $"The job is already {job.Status}.");
            case PrintJobStatus.Printing when job.PrinterJobId is { } printerJobId && job.Printer is not null:
                await ippClient.CancelJobAsync(new Uri(job.Printer.PrinterUri), printerJobId, cancellationToken);
                break;
        }

        // Only cancel if the worker hasn't moved the job on since we read it (Status is also the concurrency token).
        var now = DateTime.UtcNow;
        var previousStatus = job.Status;
        var updated = await dbContext.PrintJobs
            .Where(j => j.Id == id && j.Status == previousStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, PrintJobStatus.Canceled)
                    .SetProperty(j => j.StatusMessage, "Canceled via the API.")
                    .SetProperty(j => j.NextAttemptAt, (DateTime?)null)
                    .SetProperty(j => j.FinishedAt, now)
                    .SetProperty(j => j.UpdatedAt, now),
                cancellationToken);
        if (updated == 0)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: "The job changed state while it was being canceled; check its status.");
        }

        fileStore.Delete(job.Id);
        await dbContext.Entry(job).ReloadAsync(cancellationToken);

        return Ok(ToResponseDto(job));
    }

    private int? CurrentKeyId =>
        int.TryParse(User.FindFirstValue(ApiKeyClaimTypes.KeyId), out var keyId) ? keyId : null;

    private IQueryable<PrintJob> VisibleJobs()
    {
        if (User.HasClaim(ApiKeyClaimTypes.KeyType, ApiKeyClaimTypes.Master))
        {
            return dbContext.PrintJobs;
        }

        var keyId = CurrentKeyId;
        return keyId is null
            ? dbContext.PrintJobs.Where(_ => false)
            : dbContext.PrintJobs.Where(j => j.SubmittedByKeyId == keyId);
    }

    /// <summary>Decodes base64, accepting an optional <c>data:&lt;media type&gt;;base64,</c> prefix whose media type is returned.</summary>
    private static bool TryDecodeBase64(string value, out byte[] bytes, out string? declaredMediaType)
    {
        var base64 = value.AsSpan().Trim();
        declaredMediaType = null;

        if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var prefixEnd = base64.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
            if (prefixEnd < 0)
            {
                bytes = [];
                return false;
            }

            declaredMediaType = base64["data:".Length..prefixEnd].ToString();
            base64 = base64[(prefixEnd + ";base64,".Length)..];
        }

        var buffer = new byte[base64.Length * 3 / 4 + 3];
        if (base64.IsEmpty || !Convert.TryFromBase64Chars(base64, buffer, out var written))
        {
            bytes = [];
            return false;
        }

        bytes = buffer[..written];
        return true;
    }

    private static PrintJobResponseDto ToResponseDto(PrintJob job) => new()
    {
        Id = job.Id,
        PrinterId = job.PrinterId,
        PrinterName = job.Printer?.Name,
        DocumentSizeBytes = job.DocumentSizeBytes,
        PageCount = job.PageCount,
        Pages = job.Pages,
        PagesToPrint = PageRanges.TryParse(job.Pages, job.PageCount, out var ranges, out _) ? PageRanges.CountPages(ranges) : job.PageCount,
        Copies = job.Copies,
        Color = job.Color,
        Duplex = job.Duplex,
        Status = job.Status.ToString(),
        StatusMessage = job.StatusMessage,
        IsFinished = job.Status is PrintJobStatus.Completed or PrintJobStatus.Failed or PrintJobStatus.Canceled,
        Attempts = job.Attempts,
        NextAttemptAt = job.NextAttemptAt,
        DocumentFormatSent = job.DocumentFormatSent,
        PrinterJobId = job.PrinterJobId,
        PrinterJobState = job.PrinterJobState,
        PrinterJobStateReasons = job.PrinterJobStateReasons,
        PrinterImpressionsCompleted = job.PrinterImpressionsCompleted,
        SubmittedBy = job.SubmittedBy,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt,
        StartedAt = job.StartedAt,
        SentAt = job.SentAt,
        FinishedAt = job.FinishedAt
    };
}
