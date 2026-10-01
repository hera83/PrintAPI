using System.ComponentModel.DataAnnotations;

namespace api.Dtos.Print;

/// <summary>Only <see cref="PrinterId"/> and <see cref="DocumentBase64"/> are required; every other field has a default.</summary>
public class SubmitPrintJobRequestDto
{
    /// <summary>Id of a registered printer (see <c>GET /Hp/GetAll</c>).</summary>
    [Required]
    public int? PrinterId { get; set; }

    /// <summary>The PDF, base64-encoded. A <c>data:application/pdf;base64,</c> prefix is allowed. Anything that isn't a PDF is rejected.</summary>
    [Required]
    public string DocumentBase64 { get; set; } = string.Empty;

    /// <summary>Number of copies. Default 1.</summary>
    [Range(1, 99)]
    public int Copies { get; set; } = 1;

    /// <summary>Print in color. Default false (black and white).</summary>
    public bool Color { get; set; }

    /// <summary>Print on both sides of the paper (flipped on the long edge, like a book). Default false.</summary>
    public bool Duplex { get; set; }

    /// <summary>Which pages to print, e.g. <c>2-9</c>, <c>5</c>, <c>1,3,5-7</c> or <c>3-</c> (page 3 to the end). Default: all pages.</summary>
    [StringLength(200)]
    public string? Pages { get; set; }
}
