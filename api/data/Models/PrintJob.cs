namespace api.Data.Models;

public class PrintJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int PrinterId { get; set; }
    public HpPrinter? Printer { get; set; }

    public long DocumentSizeBytes { get; set; }

    /// <summary>Number of pages in the PDF.</summary>
    public int PageCount { get; set; }

    public int Copies { get; set; } = 1;
    public bool Color { get; set; }
    public bool Duplex { get; set; }

    /// <summary>Normalized page selection, e.g. <c>2-9</c> or <c>1,3,5-7</c>; null = all pages.</summary>
    public string? Pages { get; set; }

    public PrintJobStatus Status { get; set; } = PrintJobStatus.Queued;
    public string? StatusMessage { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// What is/was sent: <c>application/pdf</c> (printer reads PDF) or <c>image/pwg-raster</c> (rendered by the API). Set just before
    /// transmission starts, so a non-null value on an interrupted job means it may have partly printed and must not be resent.
    /// </summary>
    public string? DocumentFormatSent { get; set; }
    public int? PrinterJobId { get; set; }
    public string? PrinterJobState { get; set; }
    public List<string> PrinterJobStateReasons { get; set; } = [];
    public int? PrinterImpressionsCompleted { get; set; }

    /// <summary>Id of the standard API key that submitted the job; null when submitted with the master key.</summary>
    public int? SubmittedByKeyId { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
