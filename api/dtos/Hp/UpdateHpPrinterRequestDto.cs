using System.ComponentModel.DataAnnotations;

namespace api.Dtos.Hp;

/// <summary>Every field is optional; only the fields you send are changed.</summary>
public class UpdateHpPrinterRequestDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; set; }

    /// <summary>Send an empty string to remove the note.</summary>
    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>False deactivates the printer, so nothing is printed on it until it's activated again.</summary>
    public bool? IsActive { get; set; }

    /// <summary>New IP address or hostname, if the printer has moved. The printer is read again at the new address.</summary>
    [StringLength(255)]
    public string? Host { get; set; }
}
