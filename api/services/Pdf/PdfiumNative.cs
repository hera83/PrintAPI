using System.Runtime.InteropServices;

namespace api.Services.Pdf;

/// <summary>
/// P/Invoke declarations for the subset of the PDFium C API we use (fpdfview.h). The native library comes from
/// the <c>bblanchon.PDFium.*</c> packages. PDFium is not thread-safe — callers must hold <see cref="PdfDocument"/>'s lock.
/// </summary>
internal static class PdfiumNative
{
    private const string Library = "pdfium";

    public const int RenderAnnotations = 0x01; // FPDF_ANNOT
    public const int RenderForPrinting = 0x800; // FPDF_PRINTING

    [DllImport(Library)]
    public static extern void FPDF_InitLibrary();

    [DllImport(Library)]
    public static extern IntPtr FPDF_LoadMemDocument64(IntPtr dataBuffer, nuint size, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

    [DllImport(Library)]
    public static extern CULong FPDF_GetLastError();

    [DllImport(Library)]
    public static extern void FPDF_CloseDocument(IntPtr document);

    [DllImport(Library)]
    public static extern int FPDF_GetPageCount(IntPtr document);

    [DllImport(Library)]
    public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    [DllImport(Library)]
    public static extern void FPDF_ClosePage(IntPtr page);

    [DllImport(Library)]
    public static extern float FPDF_GetPageWidthF(IntPtr page);

    [DllImport(Library)]
    public static extern float FPDF_GetPageHeightF(IntPtr page);

    [DllImport(Library)]
    public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

    [DllImport(Library)]
    public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, CULong color);

    [DllImport(Library)]
    public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [DllImport(Library)]
    public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

    [DllImport(Library)]
    public static extern int FPDFBitmap_GetStride(IntPtr bitmap);

    [DllImport(Library)]
    public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
}
