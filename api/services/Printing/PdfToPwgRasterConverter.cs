using System.Globalization;
using System.Text.RegularExpressions;
using api.Services.Ipp;
using api.Services.Pdf;

namespace api.Services.Printing;

public sealed class PwgConversionOptions
{
    /// <summary>PWG media name, e.g. <c>iso_a4_210x297mm</c>; its trailing <c>WxHmm</c>/<c>WxHin</c> part gives the page size.</summary>
    public string Media { get; init; } = "iso_a4_210x297mm";
    public int Dpi { get; init; } = 300;
    public bool Color { get; init; } = true;
    public bool Duplex { get; init; }
    public bool Tumble { get; init; }

    /// <summary>The printer's <c>pwg-raster-document-sheet-back</c>: how it expects duplex back sides to be oriented.</summary>
    public string SheetBack { get; init; } = "normal";

    public int PrintQuality { get; init; }

    /// <summary>0-based indexes of the pages to print, in order; null = every page.</summary>
    public IReadOnlyList<int>? PageIndexes { get; init; }
}

/// <summary>Renders a PDF with PDFium and writes it as multi-page PWG Raster, for printers that can't read PDF themselves.</summary>
public static partial class PdfToPwgRasterConverter
{
    public static void Convert(PdfDocument document, Stream output, PwgConversionOptions options, CancellationToken cancellationToken)
    {
        var (widthInches, heightInches) = ParseMediaSizeInches(options.Media) ?? (210 / 25.4, 297 / 25.4);
        var width = (int)Math.Round(widthInches * options.Dpi);
        var height = (int)Math.Round(heightInches * options.Dpi);

        var writer = new PwgRasterWriter(output);
        writer.WriteSyncWord();

        var row = new byte[width * 4];
        var pageIndexes = options.PageIndexes ?? Enumerable.Range(0, document.PageCount).ToList();

        for (var sheetSide = 0; sheetSide < pageIndexes.Count; sheetSide++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageIndex = pageIndexes[sheetSide];

            // Back sides are every second page *printed*, not every second page of the PDF.
            var (crossFeed, feed) = options.Duplex && sheetSide % 2 == 1
                ? BackSideTransform(options.SheetBack, options.Tumble)
                : (1, 1);

            using var bitmap = document.RenderPageToFit(pageIndex, width, height, options.Dpi);

            writer.WritePage(
                new PwgPageHeader
                {
                    Width = width,
                    Height = height,
                    Dpi = options.Dpi,
                    Color = options.Color,
                    PageWidthPoints = (int)Math.Round(widthInches * 72),
                    PageHeightPoints = (int)Math.Round(heightInches * 72),
                    PageSizeName = options.Media,
                    Duplex = options.Duplex,
                    Tumble = options.Tumble,
                    TotalPageCount = pageIndexes.Count,
                    CrossFeedTransform = crossFeed,
                    FeedTransform = feed,
                    PrintQuality = options.PrintQuality
                },
                (y, line) =>
                {
                    bitmap.CopyRow(feed < 0 ? height - 1 - y : y, row);
                    ConvertRow(row, line, width, options.Color, crossFeed < 0);
                });
        }
    }

    /// <summary>
    /// Back-side orientation per the printer's pwg-raster-document-sheet-back (same rules as CUPS/ippeveprinter);
    /// -1 means the page data is mirrored along that axis.
    /// </summary>
    private static (int CrossFeed, int Feed) BackSideTransform(string sheetBack, bool tumble) => sheetBack switch
    {
        "flipped" => tumble ? (-1, 1) : (1, -1),
        "manual-tumble" when tumble => (-1, -1),
        "rotated" when !tumble => (-1, -1),
        _ => (1, 1)
    };

    /// <summary>PDFium BGRx → packed sRGB (3 bytes) or sGray (1 byte), optionally mirrored horizontally.</summary>
    private static void ConvertRow(byte[] bgrx, Span<byte> destination, int width, bool color, bool mirror)
    {
        for (var x = 0; x < width; x++)
        {
            var source = (mirror ? width - 1 - x : x) * 4;
            var blue = bgrx[source];
            var green = bgrx[source + 1];
            var red = bgrx[source + 2];

            if (color)
            {
                destination[x * 3] = red;
                destination[x * 3 + 1] = green;
                destination[x * 3 + 2] = blue;
            }
            else
            {
                destination[x] = (byte)((red * 299 + green * 587 + blue * 114 + 500) / 1000);
            }
        }
    }

    private static (double Width, double Height)? ParseMediaSizeInches(string media)
    {
        var match = MediaSizeRegex().Match(media);
        if (!match.Success)
        {
            return null;
        }

        var width = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var height = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return match.Groups[3].Value == "mm" ? (width / 25.4, height / 25.4) : (width, height);
    }

    [GeneratedRegex(@"_(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)(mm|in)$")]
    private static partial Regex MediaSizeRegex();
}
