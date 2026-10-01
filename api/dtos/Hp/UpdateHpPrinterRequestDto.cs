using System.ComponentModel.DataAnnotations;

namespace api.Dtos.Hp;

public class UpdateHpPrinterRequestDto
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>IP address or hostname — change it if the printer got a new address, then call Refresh.</summary>
    [Required]
    [StringLength(255)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 631;

    [StringLength(200)]
    public string ResourcePath { get; set; } = "ipp/print";
}
