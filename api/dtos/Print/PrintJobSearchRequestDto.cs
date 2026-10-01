using System.ComponentModel.DataAnnotations;

namespace api.Dtos.Print;

public class PrintJobSearchRequestDto
{
    public int? PrinterId { get; set; }

    /// <summary><c>Queued</c>, <c>Processing</c>, <c>Printing</c>, <c>Completed</c>, <c>Failed</c> or <c>Canceled</c>.</summary>
    [AllowedValues("Queued", "Processing", "Printing", "Completed", "Failed", "Canceled", null)]
    public string? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 500)]
    public int PageSize { get; set; } = 50;
}
