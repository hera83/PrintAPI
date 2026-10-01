using System.ComponentModel.DataAnnotations;

namespace api.Dtos.Hp;

/// <summary>Only <see cref="Host"/> is required; everything else about the printer is read from the printer itself.</summary>
public class RegisterHpPrinterRequestDto
{
    /// <summary>IP address or hostname of the printer, e.g. <c>10.64.2.134</c>.</summary>
    [Required]
    [StringLength(255)]
    public string Host { get; set; } = string.Empty;

    /// <summary>Display name; defaults to the printer's own name.</summary>
    [StringLength(200)]
    public string? Name { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}
