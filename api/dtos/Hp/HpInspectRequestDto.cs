using System.ComponentModel.DataAnnotations;

namespace api.Dtos.Hp;

public class HpInspectRequestDto
{
    /// <summary>IP address or hostname of the printer.</summary>
    [Required]
    [StringLength(255)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 631;

    [StringLength(200)]
    public string ResourcePath { get; set; } = "ipp/print";
}
