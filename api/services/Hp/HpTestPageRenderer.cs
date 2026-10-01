using System.Globalization;
using System.Text.Json;
using api.Data.Models;
using SkiaSharp;

namespace api.Services.Hp;

/// <summary>Draws an A4 test page (printer details, color/gray patches, line weights, supply levels) as a 300 dpi JPEG.</summary>
public static class HpTestPageRenderer
{
    public const string DocumentFormat = "image/jpeg";

    private const int PageWidth = 2480; // A4 at 300 dpi
    private const int PageHeight = 3508;
    private const float Mm = 300f / 25.4f;
    private const float Margin = 15 * Mm;
    private const float ContentWidth = PageWidth - 2 * Margin;

    private static readonly SKColor Ink = new(0x22, 0x22, 0x22);
    private static readonly SKColor Muted = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Rule = new(0xC8, 0xC8, 0xC8);

    public static byte[] RenderJpeg(HpPrinter printer, string apiName, string environmentName)
    {
        using var surface = SKSurface.Create(new SKImageInfo(PageWidth, PageHeight, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var regular = SKTypeface.FromFamilyName("Arial");
        using var bold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);

        DrawMarginFrame(canvas, regular);

        var y = Margin + 40;
        y = DrawHeader(canvas, regular, bold, printer, apiName, environmentName, y);
        y = DrawDetails(canvas, regular, bold, printer, y + 60);
        y = DrawColorPatches(canvas, regular, bold, y + 70);
        y = DrawGrayRamp(canvas, regular, bold, y + 70);
        y = DrawGradient(canvas, regular, bold, y + 70);
        y = DrawLineWeights(canvas, regular, bold, y + 70);
        DrawSupplies(canvas, regular, bold, printer, y + 70);
        DrawFooter(canvas, regular, apiName);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        return data.ToArray();
    }

    /// <summary>Thin frame 5 mm from the paper edge — if all four sides print, nothing is being clipped.</summary>
    private static void DrawMarginFrame(SKCanvas canvas, SKTypeface regular)
    {
        using var paint = new SKPaint { Color = Rule, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
        var inset = 5 * Mm;
        canvas.DrawRect(inset, inset, PageWidth - 2 * inset, PageHeight - 2 * inset, paint);

        using var font = new SKFont(regular, 26);
        using var textPaint = new SKPaint { Color = Muted, IsAntialias = true };
        canvas.DrawText("5 mm frame", inset + 12, inset + 34, SKTextAlign.Left, font, textPaint);
    }

    private static float DrawHeader(SKCanvas canvas, SKTypeface regular, SKTypeface bold, HpPrinter printer, string apiName, string environmentName, float y)
    {
        using var titleFont = new SKFont(bold, 110);
        using var subtitleFont = new SKFont(regular, 50);
        using var inkPaint = new SKPaint { Color = Ink, IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = Muted, IsAntialias = true };

        y += 110;
        canvas.DrawText("Test page", Margin, y, SKTextAlign.Left, titleFont, inkPaint);
        y += 75;
        canvas.DrawText($"{printer.Name}", Margin, y, SKTextAlign.Left, subtitleFont, inkPaint);
        y += 65;
        canvas.DrawText($"Sent from {apiName} ({environmentName})", Margin, y, SKTextAlign.Left, subtitleFont, mutedPaint);

        y += 45;
        using var rulePaint = new SKPaint { Color = Ink, StrokeWidth = 6 };
        canvas.DrawLine(Margin, y, PageWidth - Margin, y, rulePaint);
        return y;
    }

    private static float DrawDetails(SKCanvas canvas, SKTypeface regular, SKTypeface bold, HpPrinter printer, float y)
    {
        var rows = new (string Label, string? Value)[]
        {
            ("Model", printer.MakeAndModel ?? printer.Model),
            ("Serial number", printer.SerialNumber),
            ("Firmware", printer.FirmwareVersion),
            ("Address", printer.IpAddress is not null && printer.IpAddress != printer.Host ? $"{printer.Host} ({printer.IpAddress})" : printer.Host),
            ("Printer URI", printer.PrinterUri),
            ("Network name", printer.MdnsHostName),
            ("UUID", printer.Uuid),
            ("Printer ID", printer.Id.ToString(CultureInfo.InvariantCulture)),
            ("Printed", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
        };

        using var labelFont = new SKFont(bold, 44);
        using var valueFont = new SKFont(regular, 44);
        using var inkPaint = new SKPaint { Color = Ink, IsAntialias = true };
        using var rulePaint = new SKPaint { Color = Rule, StrokeWidth = 2 };

        foreach (var (label, value) in rows)
        {
            y += 72;
            canvas.DrawText(label, Margin, y, SKTextAlign.Left, labelFont, inkPaint);
            canvas.DrawText(string.IsNullOrWhiteSpace(value) ? "–" : value, Margin + 480, y, SKTextAlign.Left, valueFont, inkPaint);
            canvas.DrawLine(Margin, y + 24, PageWidth - Margin, y + 24, rulePaint);
        }

        return y + 24;
    }

    private static float DrawColorPatches(SKCanvas canvas, SKTypeface regular, SKTypeface bold, float y)
    {
        y = DrawSectionTitle(canvas, bold, "Colors", y);

        var patches = new (string Label, SKColor Color)[]
        {
            ("Cyan", new SKColor(0x00, 0xFF, 0xFF)),
            ("Magenta", new SKColor(0xFF, 0x00, 0xFF)),
            ("Yellow", new SKColor(0xFF, 0xFF, 0x00)),
            ("Black", SKColors.Black),
            ("Red", new SKColor(0xFF, 0x00, 0x00)),
            ("Green", new SKColor(0x00, 0xFF, 0x00)),
            ("Blue", new SKColor(0x00, 0x00, 0xFF))
        };

        const float gap = 24;
        var width = (ContentWidth - gap * (patches.Length - 1)) / patches.Length;
        const float height = 220;

        using var labelFont = new SKFont(regular, 38);
        using var labelPaint = new SKPaint { Color = Ink, IsAntialias = true };

        for (var i = 0; i < patches.Length; i++)
        {
            var x = Margin + i * (width + gap);
            using var fill = new SKPaint { Color = patches[i].Color };
            canvas.DrawRect(x, y, width, height, fill);
            canvas.DrawText(patches[i].Label, x + width / 2, y + height + 50, SKTextAlign.Center, labelFont, labelPaint);
        }

        return y + height + 50;
    }

    private static float DrawGrayRamp(SKCanvas canvas, SKTypeface regular, SKTypeface bold, float y)
    {
        y = DrawSectionTitle(canvas, bold, "Grayscale", y);

        const int steps = 11;
        var width = ContentWidth / steps;
        const float height = 160;

        using var labelFont = new SKFont(regular, 34);
        using var labelPaint = new SKPaint { Color = Ink, IsAntialias = true };
        using var outline = new SKPaint { Color = Rule, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };

        for (var i = 0; i < steps; i++)
        {
            var x = Margin + i * width;
            var level = (byte)Math.Round(255 - i * 25.5);
            using var fill = new SKPaint { Color = new SKColor(level, level, level) };
            canvas.DrawRect(x, y, width, height, fill);
            canvas.DrawText($"{i * 10}%", x + width / 2, y + height + 46, SKTextAlign.Center, labelFont, labelPaint);
        }

        canvas.DrawRect(Margin, y, ContentWidth, height, outline);
        return y + height + 46;
    }

    private static float DrawGradient(SKCanvas canvas, SKTypeface regular, SKTypeface bold, float y)
    {
        y = DrawSectionTitle(canvas, bold, "Color gradient", y);

        const float height = 140;
        var colors = new[] { SKColors.Red, SKColors.Yellow, SKColors.Lime, SKColors.Cyan, SKColors.Blue, SKColors.Magenta, SKColors.Red };
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(Margin, 0),
            new SKPoint(Margin + ContentWidth, 0),
            colors,
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(Margin, y, ContentWidth, height, paint);

        return y + height;
    }

    private static float DrawLineWeights(SKCanvas canvas, SKTypeface regular, SKTypeface bold, float y)
    {
        y = DrawSectionTitle(canvas, bold, "Line weights", y);

        using var labelFont = new SKFont(regular, 36);
        using var inkPaint = new SKPaint { Color = Ink, IsAntialias = true };

        foreach (var weight in new[] { 1f, 2f, 4f, 8f, 16f })
        {
            y += 44;
            using var line = new SKPaint { Color = SKColors.Black, StrokeWidth = weight };
            canvas.DrawText($"{weight:0} px", Margin, y + 12, SKTextAlign.Left, labelFont, inkPaint);
            canvas.DrawLine(Margin + 200, y, PageWidth - Margin, y, line);
        }

        return y + 10;
    }

    private static void DrawSupplies(SKCanvas canvas, SKTypeface regular, SKTypeface bold, HpPrinter printer, float y)
    {
        var supplies = HpPrinterInfoBuilder.ParseSupplies(
            JsonSerializer.Deserialize<Dictionary<string, List<string>>>(printer.IppAttributesJson) ?? []);

        y = DrawSectionTitle(
            canvas,
            bold,
            $"Supplies (as of {printer.InfoRetrievedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)})",
            y);

        using var labelFont = new SKFont(regular, 40);
        using var inkPaint = new SKPaint { Color = Ink, IsAntialias = true };
        using var outline = new SKPaint { Color = Ink, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };

        if (supplies.Count == 0)
        {
            canvas.DrawText("The printer reported no supplies.", Margin, y + 40, SKTextAlign.Left, labelFont, inkPaint);
            return;
        }

        const float barX = Margin + 620;
        const float barHeight = 40;
        var barWidth = ContentWidth - 620 - 160;

        foreach (var supply in supplies)
        {
            canvas.DrawText(supply.Name, Margin, y + 34, SKTextAlign.Left, labelFont, inkPaint);

            // marker-colors may hold several colors ("#00FFFF#FF00FF..."); use the first.
            var colorText = supply.Color is { Length: >= 7 } ? supply.Color[..7] : null;
            var color = colorText is not null && SKColor.TryParse(colorText, out var parsed) ? parsed : SKColors.Gray;

            if (supply.LevelPercent is { } level)
            {
                using var fill = new SKPaint { Color = color };
                canvas.DrawRect(barX, y, barWidth * level / 100f, barHeight, fill);
            }

            canvas.DrawRect(barX, y, barWidth, barHeight, outline);
            canvas.DrawText(supply.LevelPercent is { } percent ? $"{percent}%" : "unknown", PageWidth - Margin, y + 34, SKTextAlign.Right, labelFont, inkPaint);
            y += 68;
        }
    }

    private static void DrawFooter(SKCanvas canvas, SKTypeface regular, string apiName)
    {
        using var font = new SKFont(regular, 38);
        using var paint = new SKPaint { Color = Muted, IsAntialias = true };
        canvas.DrawText(
            $"If you can read this page, the connection between {apiName} and the printer works.",
            PageWidth / 2f,
            PageHeight - Margin,
            SKTextAlign.Center,
            font,
            paint);
    }

    private static float DrawSectionTitle(SKCanvas canvas, SKTypeface bold, string title, float y)
    {
        using var font = new SKFont(bold, 54);
        using var paint = new SKPaint { Color = Ink, IsAntialias = true };
        canvas.DrawText(title, Margin, y + 54, SKTextAlign.Left, font, paint);
        return y + 54 + 36;
    }
}
