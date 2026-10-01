using System.Globalization;

namespace api.Services.Printing;

/// <summary>Parses page selections like <c>2-9</c>, <c>1,3,5-7</c> or <c>3-</c> into sorted, merged, 1-based ranges.</summary>
public static class PageRanges
{
    public static bool TryParse(
        string? value,
        int pageCount,
        out List<(int From, int To)> ranges,
        out string? error)
    {
        ranges = [];
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            ranges.Add((1, pageCount));
            return true;
        }

        var parsed = new List<(int From, int To)>();
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-');
            var fromText = dash < 0 ? part : part[..dash].Trim();
            var toText = dash < 0 ? part : part[(dash + 1)..].Trim();

            if (!int.TryParse(fromText, NumberStyles.None, CultureInfo.InvariantCulture, out var from)
                || (toText.Length > 0 && !int.TryParse(toText, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                error = $"'{part}' is not a page or page range. Use e.g. 2-9, 5, 1,3,5-7 or 3-.";
                return false;
            }

            var to = toText.Length == 0 ? pageCount : int.Parse(toText, CultureInfo.InvariantCulture);
            if (to < from)
            {
                error = $"'{part}' is reversed; write the lowest page first, e.g. {to}-{from}.";
                return false;
            }

            if (from < 1 || to > pageCount)
            {
                error = $"'{part}' is outside the document, which has {pageCount} page(s).";
                return false;
            }

            parsed.Add((from, to));
        }

        // IPP requires ascending, non-overlapping ranges.
        foreach (var range in parsed.OrderBy(r => r.From))
        {
            if (ranges.Count > 0 && range.From <= ranges[^1].To + 1)
            {
                ranges[^1] = (ranges[^1].From, Math.Max(ranges[^1].To, range.To));
            }
            else
            {
                ranges.Add(range);
            }
        }

        return true;
    }

    /// <summary>Canonical text form, e.g. <c>1,3,5-7</c>; null when the ranges cover the whole document.</summary>
    public static string? Format(List<(int From, int To)> ranges, int pageCount) =>
        ranges is [(1, var to)] && to == pageCount
            ? null
            : string.Join(',', ranges.Select(r => r.From == r.To ? $"{r.From}" : $"{r.From}-{r.To}"));

    public static int CountPages(List<(int From, int To)> ranges) => ranges.Sum(r => r.To - r.From + 1);

    /// <summary>0-based page indexes in print order.</summary>
    public static List<int> ToPageIndexes(List<(int From, int To)> ranges) =>
        ranges.SelectMany(r => Enumerable.Range(r.From - 1, r.To - r.From + 1)).ToList();
}
