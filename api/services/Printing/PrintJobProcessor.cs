using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using api.Data;
using api.Data.Models;
using api.Services.Ipp;
using api.Services.Ipp.Dtos;
using api.Services.Ipp.Interfaces;
using api.Services.Pdf;
using api.Services.Printing.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace api.Services.Printing;

public partial class PrintJobProcessor(
    ApiDbContext dbContext,
    IIppClient ippClient,
    PrintJobFileStore fileStore,
    ILogger<PrintJobProcessor> logger) : IPrintJobProcessor
{
    public const string PdfFormat = "application/pdf";
    public const string PwgRasterFormat = "image/pwg-raster";

    private const int MaxAttempts = 5;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];
    private static readonly TimeSpan MaxPrintingDuration = TimeSpan.FromHours(24);

    /// <summary>True if the printer can take a PDF job one way or the other (natively, or rendered to PWG Raster by us).</summary>
    public static bool CanPrintPdf(HpPrinter printer) =>
        printer.DocumentFormats.Count == 0
        || printer.DocumentFormats.Contains(PdfFormat, StringComparer.OrdinalIgnoreCase)
        || printer.DocumentFormats.Contains(PwgRasterFormat, StringComparer.OrdinalIgnoreCase);

    /// <summary>IPP print-quality "normal" (3 = draft, 5 = high) — used for every job.</summary>
    private const int DefaultPrintQuality = 4;

    public async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default)
    {
        var interrupted = await dbContext.PrintJobs
            .Where(j => j.Status == PrintJobStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var job in interrupted)
        {
            HandleInterruption(job);
        }

        if (interrupted.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Recovered {Count} print job(s) interrupted by a restart", interrupted.Count);
        }
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        await PollPrintingJobsAsync(cancellationToken);
        await DispatchQueuedJobsAsync(cancellationToken);
    }

    private async Task PollPrintingJobsAsync(CancellationToken cancellationToken)
    {
        var jobs = await dbContext.PrintJobs
            .Include(j => j.Printer)
            .Where(j => j.Status == PrintJobStatus.Printing)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            await UpdatePrintingJobAsync(job, cancellationToken);
            await TrySaveAsync(job, cancellationToken);
        }
    }

    private async Task UpdatePrintingJobAsync(PrintJob job, CancellationToken cancellationToken)
    {
        if (job.PrinterJobId is not { } printerJobId || job.Printer is null)
        {
            Finish(job, PrintJobStatus.Completed, "The printer returned no job id, so the job's progress can't be tracked.");
            return;
        }

        try
        {
            var status = await ippClient.GetJobAttributesAsync(new Uri(job.Printer.PrinterUri), printerJobId, cancellationToken);
            job.PrinterJobState = status.JobState;
            job.PrinterJobStateReasons = status.JobStateReasons;
            job.PrinterImpressionsCompleted = status.ImpressionsCompleted ?? job.PrinterImpressionsCompleted;
            job.UpdatedAt = DateTime.UtcNow;

            var detail = status.JobStateMessage ?? string.Join(", ", status.JobStateReasons);
            switch (status.JobState)
            {
                case "completed":
                    Finish(job, PrintJobStatus.Completed, null);
                    return;
                case "canceled":
                    Finish(job, PrintJobStatus.Canceled, "The job was canceled on the printer.");
                    return;
                case "aborted":
                    Finish(job, PrintJobStatus.Failed, $"The printer aborted the job: {detail}");
                    return;
                case "processing-stopped":
                    // Usually needs attention at the printer: out of paper, paper jam, door open, ...
                    job.StatusMessage = $"The printer has stopped: {detail}";
                    break;
                default:
                    job.StatusMessage = null;
                    break;
            }
        }
        catch (IppException ex) when (ex.IppStatusCode == 0x0406)
        {
            // client-error-not-found: printers only keep a short job history.
            Finish(job, PrintJobStatus.Completed, "The printer no longer reports the job; assumed completed.");
            return;
        }
        catch (IppException ex)
        {
            job.StatusMessage = $"Could not check the job's state on the printer: {ex.Message}";
            job.UpdatedAt = DateTime.UtcNow;
        }

        if (job.SentAt is { } sentAt && DateTime.UtcNow - sentAt > MaxPrintingDuration)
        {
            Finish(
                job,
                PrintJobStatus.Failed,
                $"The printer did not finish the job within {MaxPrintingDuration.TotalHours:0} hours (last state: {job.PrinterJobState ?? "unknown"}).");
        }
    }

    private async Task DispatchQueuedJobsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // One job at a time per printer, so status tracking stays accurate and jobs print in submission order.
        var busyPrinterIds = await dbContext.PrintJobs
            .Where(j => j.Status == PrintJobStatus.Printing || j.Status == PrintJobStatus.Processing)
            .Select(j => j.PrinterId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var queued = await dbContext.PrintJobs
            .Where(j => j.Status == PrintJobStatus.Queued)
            .OrderBy(j => j.CreatedAt)
            .Select(j => new { j.Id, j.PrinterId, j.NextAttemptAt })
            .ToListAsync(cancellationToken);

        // Only the oldest queued job per printer is eligible, so a job waiting for a retry isn't overtaken.
        var dueJobIds = queued
            .GroupBy(j => j.PrinterId)
            .Select(g => g.First())
            .Where(j => !busyPrinterIds.Contains(j.PrinterId) && (j.NextAttemptAt is null || j.NextAttemptAt <= now))
            .Select(j => j.Id)
            .ToList();

        foreach (var jobId in dueJobIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchAsync(jobId, cancellationToken);
        }
    }

    private async Task DispatchAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.PrintJobs
            .Include(j => j.Printer)
            .FirstOrDefaultAsync(j => j.Id == jobId && j.Status == PrintJobStatus.Queued, cancellationToken);
        if (job?.Printer is null)
        {
            return;
        }

        var printer = job.Printer;
        if (!printer.IsActive)
        {
            Finish(job, PrintJobStatus.Failed, $"Printer '{printer.Name}' has been deactivated.");
            await TrySaveAsync(job, cancellationToken);
            return;
        }

        job.Status = PrintJobStatus.Processing;
        job.Attempts++;
        job.StartedAt ??= DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        if (!await TrySaveAsync(job, cancellationToken))
        {
            return; // canceled in the meantime
        }

        try
        {
            await SendAsync(job, printer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HandleInterruption(job);
            await TrySaveAsync(job, CancellationToken.None);
            throw;
        }
        catch (IppException ex) when (ex.StatusCode == HttpStatusCode.GatewayTimeout)
        {
            // A timeout mid-transfer is ambiguous — the printer may already have printed part of the job — so never resend automatically.
            Finish(job, PrintJobStatus.Failed, $"{ex.Message} The printer may have printed part of the job, so it was not retried automatically.");
            logger.LogWarning("Print job {JobId} timed out while sending; not retrying: {Error}", job.Id, ex.Message);
        }
        catch (IppException ex) when (ex.IsTransient && job.Attempts < MaxAttempts)
        {
            var retryAt = DateTime.UtcNow + RetryDelays[Math.Min(job.Attempts - 1, RetryDelays.Length - 1)];
            job.Status = PrintJobStatus.Queued;
            job.DocumentFormatSent = null;
            job.NextAttemptAt = retryAt;
            job.StatusMessage = $"Attempt {job.Attempts} of {MaxAttempts} failed: {ex.Message} Retrying at {retryAt:yyyy-MM-dd HH:mm:ss} UTC.";
            job.UpdatedAt = DateTime.UtcNow;
            logger.LogWarning("Print job {JobId} attempt {Attempt} failed, retrying at {RetryAt}: {Error}", job.Id, job.Attempts, retryAt, ex.Message);
        }
        catch (Exception ex) when (ex is IppException or InvalidDataException)
        {
            Finish(job, PrintJobStatus.Failed, ex.Message);
            logger.LogWarning("Print job {JobId} failed: {Error}", job.Id, ex.Message);
        }
        catch (Exception ex)
        {
            Finish(job, PrintJobStatus.Failed, $"Unexpected error while sending the job: {ex.Message}");
            logger.LogError(ex, "Print job {JobId} failed unexpectedly", job.Id);
        }
        finally
        {
            File.Delete(fileStore.GetRasterPath(job.Id));
        }

        await TrySaveAsync(job, CancellationToken.None);
    }

    private async Task SendAsync(PrintJob job, HpPrinter printer, CancellationToken cancellationToken)
    {
        var documentPath = fileStore.GetDocumentPath(job.Id);
        if (!File.Exists(documentPath))
        {
            throw new InvalidDataException("The job's document is missing from app_files/PrintJobs.");
        }

        if (!PageRanges.TryParse(job.Pages, job.PageCount, out var pageRanges, out var pagesError))
        {
            throw new InvalidDataException($"Invalid page selection '{job.Pages}': {pagesError}");
        }

        var printerReadsPdf = printer.DocumentFormats.Count == 0
            || printer.DocumentFormats.Contains(PdfFormat, StringComparer.OrdinalIgnoreCase);
        var request = new IppPrintJobRequestDto
        {
            JobName = $"PrintAPI {job.Id.ToString()[..8]}",
            RequestingUserName = job.SubmittedBy,
            Copies = job.Copies,
            PrintColorMode = job.Color ? "color" : "monochrome",
            Sides = job.Duplex ? "two-sided-long-edge" : "one-sided",
            PrintQuality = DefaultPrintQuality,
            // The printer only finishes reading the job at roughly its printing speed (~35 s for 2 duplex pages on an inkjet).
            Timeout = TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30) * PageRanges.CountPages(pageRanges) * job.Copies
        };

        string documentToSend;
        if (printerReadsPdf)
        {
            documentToSend = documentPath;
            request.DocumentFormat = PdfFormat;
            request.PageRanges = job.Pages is null ? null : pageRanges; // the printer selects the pages itself
        }
        else if (printer.DocumentFormats.Contains(PwgRasterFormat, StringComparer.OrdinalIgnoreCase))
        {
            var options = BuildConversionOptions(job, printer, PageRanges.ToPageIndexes(pageRanges));
            documentToSend = fileStore.GetRasterPath(job.Id);

            await using (var output = File.Create(documentToSend))
            using (var pdf = PdfDocument.Open(await File.ReadAllBytesAsync(documentPath, cancellationToken)))
            {
                PdfToPwgRasterConverter.Convert(pdf, output, options, cancellationToken);
            }

            request.DocumentFormat = PwgRasterFormat;
            request.Media = options.Media;
        }
        else
        {
            throw new InvalidDataException(
                $"Printer '{printer.Name}' accepts neither PDF nor PWG Raster (it supports {string.Join(", ", printer.DocumentFormats)}).");
        }

        // Persisted before the first byte goes out: from here on an interruption may leave a partial printout, so the job must not be resent.
        job.DocumentFormatSent = request.DocumentFormat;
        job.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        IppPrintJobResultDto result;
        await using (var document = File.OpenRead(documentToSend))
        {
            request.Document = document;
            result = await ippClient.PrintJobAsync(new Uri(printer.PrinterUri), request, cancellationToken);
        }

        job.PrinterJobId = result.JobId;
        job.PrinterJobState = result.JobState;
        job.PrinterJobStateReasons = result.JobStateReasons;
        job.SentAt = DateTime.UtcNow;
        job.NextAttemptAt = null;
        job.UpdatedAt = DateTime.UtcNow;

        if (result.JobId is null)
        {
            Finish(job, PrintJobStatus.Completed, "The printer accepted the job but returned no job id, so its progress can't be tracked.");
        }
        else
        {
            job.Status = PrintJobStatus.Printing;
            job.StatusMessage = null;
        }

        logger.LogInformation(
            "Print job {JobId} sent to printer {PrinterId} as {Format}: printer job {PrinterJobId}, state {PrinterJobState}",
            job.Id, printer.Id, request.DocumentFormat, result.JobId, result.JobState);
    }

    /// <summary>Picks resolution, color space, media and duplex orientation from the printer's pwg-raster-* attributes.</summary>
    private static PwgConversionOptions BuildConversionOptions(PrintJob job, HpPrinter printer, List<int> pageIndexes)
    {
        var ipp = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(printer.IppAttributesJson) ?? [];
        var types = ipp.GetValueOrDefault("pwg-raster-document-type-supported") ?? [];
        var supportsColor = types.Count == 0 || types.Contains("srgb_8");
        var supportsGray = types.Count == 0 || types.Contains("sgray_8");

        var resolutions = (ipp.GetValueOrDefault("pwg-raster-document-resolution-supported") ?? [])
            .Select(r => ResolutionRegex().Match(r))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

        return new PwgConversionOptions
        {
            Media = printer.MediaDefault ?? "iso_a4_210x297mm",
            // 300 dpi is plenty for documents and keeps the raster a quarter of the size of 600 dpi.
            Dpi = resolutions.Count == 0 || resolutions.Contains(300) ? 300 : resolutions.Min(),
            Color = job.Color ? supportsColor || !supportsGray : !supportsGray,
            Duplex = job.Duplex,
            Tumble = false, // long-edge binding
            SheetBack = ipp.GetValueOrDefault("pwg-raster-document-sheet-back")?.FirstOrDefault() ?? "normal",
            PrintQuality = DefaultPrintQuality,
            PageIndexes = pageIndexes
        };
    }

    /// <summary>
    /// A job interrupted (shutdown/crash) before transmission started is safely requeued; one interrupted mid-transmission
    /// may have partly printed, so it is failed instead of risking a duplicate printout.
    /// </summary>
    private void HandleInterruption(PrintJob job)
    {
        if (job.DocumentFormatSent is null)
        {
            job.Status = PrintJobStatus.Queued;
            job.StatusMessage = "Requeued: the API stopped before the job was sent to the printer.";
            job.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            Finish(job, PrintJobStatus.Failed, "The API stopped while sending the job. The printer may have printed part of it, so it was not resent automatically.");
        }
    }

    /// <summary>Moves the job to a final state and deletes its files.</summary>
    private void Finish(PrintJob job, PrintJobStatus status, string? message)
    {
        job.Status = status;
        job.StatusMessage = message;
        job.NextAttemptAt = null;
        job.FinishedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        fileStore.Delete(job.Id);
    }

    /// <summary>Saves the job unless someone else (e.g. the Cancel endpoint) changed its status since it was loaded.</summary>
    private async Task<bool> TrySaveAsync(PrintJob job, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Print job {JobId} changed status concurrently; skipping this update", job.Id);
            dbContext.Entry(job).State = EntityState.Detached;
            return false;
        }
    }

    [GeneratedRegex(@"^(\d+)x\d+dpi$")]
    private static partial Regex ResolutionRegex();
}
