namespace api.Dtos.Print;

public class PrintJobResponseDto
{
    public Guid Id { get; set; }
    public int PrinterId { get; set; }
    public string? PrinterName { get; set; }
    public long DocumentSizeBytes { get; set; }

    /// <summary>Number of pages in the PDF.</summary>
    public int PageCount { get; set; }

    /// <summary>The selected pages, e.g. <c>2-9</c>; null = all pages.</summary>
    public string? Pages { get; set; }

    /// <summary>Number of pages that will be printed per copy.</summary>
    public int PagesToPrint { get; set; }

    public int Copies { get; set; }
    public bool Color { get; set; }
    public bool Duplex { get; set; }

    /// <summary><c>Queued</c>, <c>Processing</c>, <c>Printing</c>, <c>Completed</c>, <c>Failed</c> or <c>Canceled</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Why the job failed, why it's waiting for a retry, or what the printer is stuck on (e.g. out of paper).</summary>
    public string? StatusMessage { get; set; }

    /// <summary>True once the job can no longer change (Completed, Failed or Canceled).</summary>
    public bool IsFinished { get; set; }

    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    public string? DocumentFormatSent { get; set; }
    public int? PrinterJobId { get; set; }
    public string? PrinterJobState { get; set; }
    public List<string> PrinterJobStateReasons { get; set; } = [];
    public int? PrinterImpressionsCompleted { get; set; }

    public string SubmittedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
