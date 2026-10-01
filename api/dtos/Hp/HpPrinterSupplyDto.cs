namespace api.Dtos.Hp;

public class HpPrinterSupplyDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>IPP marker type, e.g. <c>toner</c>, <c>ink-cartridge</c>, <c>opc</c>.</summary>
    public string? Type { get; set; }
    /// <summary>Hex color(s), e.g. <c>#000000</c>.</summary>
    public string? Color { get; set; }
    /// <summary>Remaining level in percent; null when the printer doesn't report it.</summary>
    public int? LevelPercent { get; set; }
}
