namespace api.Services.Ipp.Dtos;

public sealed class IppPrintJobRequestDto
{
    /// <summary>The document data, streamed to the printer from its current position.</summary>
    public Stream Document { get; set; } = Stream.Null;

    /// <summary>MIME type of <see cref="Document"/>; must be one of the printer's <c>document-format-supported</c>.</summary>
    public string DocumentFormat { get; set; } = "application/octet-stream";

    public string JobName { get; set; } = "Print API job";

    public string RequestingUserName { get; set; } = "PrintAPI";

    /// <summary>
    /// How long to wait for the printer to accept the whole job. Inkjets read the document at printing speed, so
    /// this must grow with the page count; null uses the client default.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    public int? Copies { get; set; }

    /// <summary>Media keyword, e.g. <c>iso_a4_210x297mm</c>; null uses the printer default.</summary>
    public string? Media { get; set; }

    /// <summary><c>color</c>, <c>monochrome</c>, <c>auto</c>, ...; null uses the printer default.</summary>
    public string? PrintColorMode { get; set; }

    /// <summary><c>auto</c>, <c>fit</c>, <c>fill</c>, <c>none</c>, ...; null uses the printer default.</summary>
    public string? PrintScaling { get; set; }

    /// <summary><c>one-sided</c>, <c>two-sided-long-edge</c>, <c>two-sided-short-edge</c>; null uses the printer default.</summary>
    public string? Sides { get; set; }

    /// <summary>IPP print-quality enum: 3 draft, 4 normal, 5 high; null uses the printer default.</summary>
    public int? PrintQuality { get; set; }

    /// <summary>1-based, ascending, non-overlapping page ranges to print; null prints every page.</summary>
    public IReadOnlyList<(int From, int To)>? PageRanges { get; set; }
}
