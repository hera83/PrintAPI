using System.Buffers.Binary;
using System.Text;

namespace api.Services.Ipp;

/// <summary>Per-page settings written into a PWG Raster page header.</summary>
public sealed class PwgPageHeader
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Dpi { get; init; } = 300;

    /// <summary>true = sRGB 8-bit (srgb_8), false = sGray 8-bit (sgray_8).</summary>
    public bool Color { get; init; } = true;

    public int PageWidthPoints { get; init; }
    public int PageHeightPoints { get; init; }

    /// <summary>PWG media size name, e.g. <c>iso_a4_210x297mm</c>.</summary>
    public string PageSizeName { get; init; } = string.Empty;

    public bool Duplex { get; init; }
    public bool Tumble { get; init; }
    public int TotalPageCount { get; init; }

    /// <summary>-1 when the page data is mirrored horizontally (duplex back side), otherwise 1.</summary>
    public int CrossFeedTransform { get; init; } = 1;

    /// <summary>-1 when the page data is mirrored vertically (duplex back side), otherwise 1.</summary>
    public int FeedTransform { get; init; } = 1;

    /// <summary>IPP print-quality: 3 draft, 4 normal, 5 high; 0 = printer default.</summary>
    public int PrintQuality { get; init; }
}

/// <summary>
/// Writes PWG Raster (PWG 5102.4), the multi-page raster format every IPP Everywhere printer accepts as
/// <c>image/pwg-raster</c>: a "RaS2" sync word, then per page a 1796-byte header and PackBits-style compressed lines.
/// </summary>
public sealed class PwgRasterWriter(Stream output)
{
    private const int HeaderSize = 1796;
    private const uint ColorSpaceSGray = 18;
    private const uint ColorSpaceSrgb = 19;

    /// <summary>Fills <paramref name="line"/> with row <paramref name="y"/> of the page, packed in the page's color space.</summary>
    public delegate void LineReader(int y, Span<byte> line);

    public void WriteSyncWord() => output.Write("RaS2"u8);

    public void WritePage(PwgPageHeader header, LineReader readLine)
    {
        var bytesPerPixel = header.Color ? 3 : 1;
        var bytesPerLine = header.Width * bytesPerPixel;

        output.Write(BuildHeader(header, bytesPerPixel, bytesPerLine));

        var previous = new byte[bytesPerLine];
        var current = new byte[bytesPerLine];
        // Worst case: every pixel literal, one control byte per 128 pixels, plus the line-repeat byte.
        var encoded = new byte[bytesPerLine + header.Width / 128 + 2];

        readLine(0, previous);
        var repeat = 0;

        for (var y = 1; y < header.Height; y++)
        {
            readLine(y, current);
            if (repeat < 255 && current.AsSpan().SequenceEqual(previous))
            {
                repeat++;
                continue;
            }

            WriteLine(previous, repeat, bytesPerPixel, encoded);
            (previous, current) = (current, previous);
            repeat = 0;
        }

        WriteLine(previous, repeat, bytesPerPixel, encoded);
    }

    private static byte[] BuildHeader(PwgPageHeader header, int bytesPerPixel, int bytesPerLine)
    {
        var buffer = new byte[HeaderSize];

        PutString(buffer, 0, "PwgRaster");
        PutUInt32(buffer, 272, header.Duplex ? 1u : 0u);
        PutUInt32(buffer, 276, (uint)header.Dpi); // HWResolution
        PutUInt32(buffer, 280, (uint)header.Dpi);
        PutUInt32(buffer, 340, 1); // NumCopies (copies are requested via IPP instead)
        PutUInt32(buffer, 352, (uint)header.PageWidthPoints);
        PutUInt32(buffer, 356, (uint)header.PageHeightPoints);
        PutUInt32(buffer, 368, header.Tumble ? 1u : 0u);
        PutUInt32(buffer, 372, (uint)header.Width);
        PutUInt32(buffer, 376, (uint)header.Height);
        PutUInt32(buffer, 384, 8); // BitsPerColor
        PutUInt32(buffer, 388, (uint)(bytesPerPixel * 8)); // BitsPerPixel
        PutUInt32(buffer, 392, (uint)bytesPerLine);
        PutUInt32(buffer, 396, 0); // ColorOrder: chunky
        PutUInt32(buffer, 400, header.Color ? ColorSpaceSrgb : ColorSpaceSGray);
        PutUInt32(buffer, 420, (uint)bytesPerPixel); // NumColors
        PutUInt32(buffer, 452, (uint)header.TotalPageCount);
        PutInt32(buffer, 456, header.CrossFeedTransform);
        PutInt32(buffer, 460, header.FeedTransform);
        PutUInt32(buffer, 464, 0); // ImageBoxLeft
        PutUInt32(buffer, 468, 0); // ImageBoxTop
        PutUInt32(buffer, 472, (uint)header.Width); // ImageBoxRight
        PutUInt32(buffer, 476, (uint)header.Height); // ImageBoxBottom
        PutUInt32(buffer, 480, 0x00FFFFFF); // AlternatePrimary: white
        PutUInt32(buffer, 484, (uint)header.PrintQuality);
        PutString(buffer, 1732, header.PageSizeName);

        return buffer;
    }

    /// <summary>
    /// One line-repeat byte (line is used repeat+1 times), then runs: 0..127 = next pixel repeated n+1 times,
    /// 129..255 = 257-n literal pixels follow.
    /// </summary>
    private void WriteLine(byte[] line, int repeat, int bytesPerPixel, byte[] encoded)
    {
        var length = 0;
        encoded[length++] = (byte)repeat;

        var pixelCount = line.Length / bytesPerPixel;
        var x = 0;

        while (x < pixelCount)
        {
            if (x + 1 < pixelCount && PixelEquals(line, x, x + 1, bytesPerPixel))
            {
                var count = 2;
                while (x + count < pixelCount && count < 128 && PixelEquals(line, x, x + count, bytesPerPixel))
                {
                    count++;
                }

                encoded[length++] = (byte)(count - 1);
                line.AsSpan(x * bytesPerPixel, bytesPerPixel).CopyTo(encoded.AsSpan(length));
                length += bytesPerPixel;
                x += count;
            }
            else
            {
                var start = x;
                var count = 1;
                x++;
                while (x < pixelCount && count < 128 && !(x + 1 < pixelCount && PixelEquals(line, x, x + 1, bytesPerPixel)))
                {
                    count++;
                    x++;
                }

                encoded[length++] = count == 1 ? (byte)0 : (byte)(257 - count);
                line.AsSpan(start * bytesPerPixel, count * bytesPerPixel).CopyTo(encoded.AsSpan(length));
                length += count * bytesPerPixel;
            }
        }

        output.Write(encoded, 0, length);
    }

    private static bool PixelEquals(byte[] line, int a, int b, int bytesPerPixel) =>
        line.AsSpan(a * bytesPerPixel, bytesPerPixel).SequenceEqual(line.AsSpan(b * bytesPerPixel, bytesPerPixel));

    private static void PutString(byte[] buffer, int offset, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, 63)).CopyTo(buffer.AsSpan(offset)); // 64-byte NUL-terminated field
    }

    private static void PutUInt32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), value);

    private static void PutInt32(byte[] buffer, int offset, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), value);
}
