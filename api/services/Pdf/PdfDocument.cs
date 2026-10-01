using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace api.Services.Pdf;

/// <summary>A PDF loaded into PDFium. All PDFium calls are serialized through one process-wide lock.</summary>
public sealed class PdfDocument : IDisposable
{
    internal static readonly Lock SyncRoot = new();
    private static bool _libraryInitialized;

    private IntPtr _buffer;
    private IntPtr _handle;

    private PdfDocument(IntPtr buffer, IntPtr handle, int pageCount)
    {
        _buffer = buffer;
        _handle = handle;
        PageCount = pageCount;
    }

    public int PageCount { get; }

    public static PdfDocument Open(byte[] data) =>
        TryOpen(data, out var document, out var error) ? document : throw new InvalidDataException(error);

    public static bool TryOpen(byte[] data, [NotNullWhen(true)] out PdfDocument? document, [NotNullWhen(false)] out string? error)
    {
        document = null;
        error = null;

        if (data.Length == 0)
        {
            error = "The document is empty.";
            return false;
        }

        lock (SyncRoot)
        {
            if (!_libraryInitialized)
            {
                PdfiumNative.FPDF_InitLibrary();
                _libraryInitialized = true;
            }

            // PDFium reads from the buffer for the document's whole lifetime, so it must outlive the handle.
            var buffer = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, buffer, data.Length);

            var handle = PdfiumNative.FPDF_LoadMemDocument64(buffer, (nuint)data.Length, null);
            if (handle == IntPtr.Zero)
            {
                var code = PdfiumNative.FPDF_GetLastError().Value;
                Marshal.FreeHGlobal(buffer);
                error = code switch
                {
                    3 => "The file is not a PDF or is corrupted.",
                    4 => "The PDF is password protected.",
                    5 => "The PDF uses an unsupported security scheme.",
                    _ => $"PDFium could not open the document (error {code})."
                };
                return false;
            }

            var pageCount = PdfiumNative.FPDF_GetPageCount(handle);
            if (pageCount <= 0)
            {
                PdfiumNative.FPDF_CloseDocument(handle);
                Marshal.FreeHGlobal(buffer);
                error = "The PDF has no pages.";
                return false;
            }

            document = new PdfDocument(buffer, handle, pageCount);
            return true;
        }
    }

    /// <summary>
    /// Renders one page onto a white <paramref name="targetWidth"/>×<paramref name="targetHeight"/> pixel canvas at
    /// <paramref name="dpi"/>: rotated 90° when its orientation differs from the canvas, scaled down (never up) to fit, and centered.
    /// </summary>
    public PdfBitmap RenderPageToFit(int pageIndex, int targetWidth, int targetHeight, int dpi)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        lock (SyncRoot)
        {
            var page = PdfiumNative.FPDF_LoadPage(_handle, pageIndex);
            if (page == IntPtr.Zero)
            {
                throw new InvalidDataException($"PDFium could not load page {pageIndex + 1}.");
            }

            try
            {
                var widthPoints = PdfiumNative.FPDF_GetPageWidthF(page);
                var heightPoints = PdfiumNative.FPDF_GetPageHeightF(page);
                var rotate = widthPoints > heightPoints != targetWidth > targetHeight;
                if (rotate)
                {
                    (widthPoints, heightPoints) = (heightPoints, widthPoints);
                }

                var naturalWidth = widthPoints / 72f * dpi;
                var naturalHeight = heightPoints / 72f * dpi;
                var scale = Math.Min(1f, Math.Min(targetWidth / naturalWidth, targetHeight / naturalHeight));
                var sizeX = (int)Math.Round(naturalWidth * scale);
                var sizeY = (int)Math.Round(naturalHeight * scale);

                var bitmap = PdfiumNative.FPDFBitmap_Create(targetWidth, targetHeight, 0);
                if (bitmap == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"PDFium could not allocate a {targetWidth}x{targetHeight} bitmap.");
                }

                PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, targetWidth, targetHeight, new CULong(0xFFFFFFFF));
                PdfiumNative.FPDF_RenderPageBitmap(
                    bitmap,
                    page,
                    (targetWidth - sizeX) / 2,
                    (targetHeight - sizeY) / 2,
                    sizeX,
                    sizeY,
                    rotate ? 1 : 0,
                    PdfiumNative.RenderAnnotations | PdfiumNative.RenderForPrinting);

                return new PdfBitmap(
                    bitmap,
                    targetWidth,
                    targetHeight,
                    PdfiumNative.FPDFBitmap_GetStride(bitmap),
                    PdfiumNative.FPDFBitmap_GetBuffer(bitmap));
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    public void Dispose()
    {
        lock (SyncRoot)
        {
            if (_handle != IntPtr.Zero)
            {
                PdfiumNative.FPDF_CloseDocument(_handle);
                _handle = IntPtr.Zero;
            }

            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
            }
        }
    }
}

/// <summary>A rendered page in PDFium's 32-bit BGRx layout.</summary>
public sealed class PdfBitmap : IDisposable
{
    private IntPtr _handle;
    private readonly IntPtr _buffer;

    internal PdfBitmap(IntPtr handle, int width, int height, int stride, IntPtr buffer)
    {
        _handle = handle;
        _buffer = buffer;
        Width = width;
        Height = height;
        Stride = stride;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    /// <summary>Copies row <paramref name="y"/> (Width × 4 bytes, B G R x) into <paramref name="destination"/>.</summary>
    public void CopyRow(int y, byte[] destination)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        Marshal.Copy(_buffer + y * Stride, destination, 0, Width * 4);
    }

    public void Dispose()
    {
        lock (PdfDocument.SyncRoot)
        {
            if (_handle != IntPtr.Zero)
            {
                PdfiumNative.FPDFBitmap_Destroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
