using System.Globalization;
using api.Dtos.Hp;

namespace api.Services.Hp;

/// <summary>Turns raw IPP attributes + mDNS TXT records into the normalized fields of <see cref="HpPrinterInfoDto"/>.</summary>
public static class HpPrinterInfoBuilder
{
    public static Uri BuildPrinterUri(string host, int port, string resourcePath) =>
        new UriBuilder("ipp", host, port, NormalizeResourcePath(resourcePath)).Uri;

    public static string NormalizeResourcePath(string? resourcePath) => (resourcePath ?? string.Empty).Trim().Trim('/');

    public static bool IsHpManufacturer(string? manufacturer, string? makeAndModel) =>
        IsHpName(manufacturer) || IsHpName(makeAndModel);

    public static void Populate(
        HpPrinterInfoDto target,
        string host,
        string? ipAddress,
        int port,
        string resourcePath,
        Dictionary<string, List<string>>? ippAttributes,
        IReadOnlyDictionary<string, string> mdnsTxtRecords,
        List<string> mdnsServices,
        string? mdnsHostName,
        string? mdnsDisplayName)
    {
        var ipp = ippAttributes ?? [];
        var txt = new Dictionary<string, string>(mdnsTxtRecords, StringComparer.OrdinalIgnoreCase);

        var deviceIdString = First(ipp, "printer-device-id");
        var deviceId = ParseDeviceId(deviceIdString);
        var makeAndModel = First(ipp, "printer-make-and-model") ?? Txt(txt, "ty");
        var manufacturer = deviceId.GetValueOrDefault("MFG")
            ?? deviceId.GetValueOrDefault("MANUFACTURER")
            ?? Txt(txt, "usb_MFG")
            ?? makeAndModel?.Split(' ', 2)[0];
        var sidesSupported = Values(ipp, "sides-supported");

        resourcePath = NormalizeResourcePath(resourcePath);

        target.Host = host;
        target.IpAddress = ipAddress;
        target.MdnsHostName = mdnsHostName;
        target.Port = port;
        target.ResourcePath = resourcePath;
        target.PrinterUri = BuildPrinterUri(host, port, resourcePath).ToString();

        target.PrinterName = First(ipp, "printer-name") ?? mdnsDisplayName;
        target.Manufacturer = manufacturer;
        target.Model = deviceId.GetValueOrDefault("MDL") ?? deviceId.GetValueOrDefault("MODEL") ?? Txt(txt, "usb_MDL") ?? makeAndModel;
        target.MakeAndModel = makeAndModel;
        target.SerialNumber = deviceId.GetValueOrDefault("SN")
            ?? deviceId.GetValueOrDefault("SERN")
            ?? deviceId.GetValueOrDefault("SERIALNUMBER")
            ?? First(ipp, "printer-serial-number");
        target.Uuid = StripUuidPrefix(First(ipp, "printer-uuid") ?? Txt(txt, "UUID"));
        target.DeviceId = deviceIdString;
        target.FirmwareVersion = Joined(ipp, "printer-firmware-string-version") ?? Joined(ipp, "printer-firmware-version");
        target.Location = First(ipp, "printer-location") ?? Txt(txt, "note");
        target.Info = First(ipp, "printer-info");
        target.AdminUrl = First(ipp, "printer-more-info") ?? Txt(txt, "adminurl");

        target.State = First(ipp, "printer-state") switch
        {
            "3" => "idle",
            "4" => "processing",
            "5" => "stopped",
            var other => other
        };
        target.StateReasons = Values(ipp, "printer-state-reasons");
        target.StateMessage = First(ipp, "printer-state-message");

        target.SupportsColor = ParseBool(First(ipp, "color-supported")) ?? ParseBool(Txt(txt, "Color"));
        target.SupportsDuplex = sidesSupported.Count > 0
            ? sidesSupported.Any(s => s.StartsWith("two-sided", StringComparison.OrdinalIgnoreCase))
            : ParseBool(Txt(txt, "Duplex"));
        target.DocumentFormats = Values(ipp, "document-format-supported") is { Count: > 0 } formats
            ? formats
            : (Txt(txt, "pdl") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        target.MediaSupported = Values(ipp, "media-supported");
        target.MediaDefault = First(ipp, "media-default");
        target.Resolutions = Values(ipp, "printer-resolution-supported");
        target.IppVersions = Values(ipp, "ipp-versions-supported");

        target.Supplies = ParseSupplies(ipp);
        target.MdnsServices = mdnsServices;
        target.IsHp = IsHpManufacturer(manufacturer, makeAndModel);
        target.IppAttributes = ipp;
        target.MdnsTxtRecords = txt;
        target.InfoRetrievedAt = DateTime.UtcNow;
    }

    /// <summary>Builds the supply list from the parallel <c>marker-*</c> IPP attributes.</summary>
    public static List<HpPrinterSupplyDto> ParseSupplies(IReadOnlyDictionary<string, List<string>> ipp)
    {
        var types = Values(ipp, "marker-types");
        var colors = Values(ipp, "marker-colors");
        var levels = Values(ipp, "marker-levels");
        var highLevels = Values(ipp, "marker-high-levels");

        return Values(ipp, "marker-names")
            .Select((name, i) => new HpPrinterSupplyDto
            {
                Name = name,
                Type = types.ElementAtOrDefault(i),
                Color = colors.ElementAtOrDefault(i),
                LevelPercent = ToPercent(levels.ElementAtOrDefault(i), highLevels.ElementAtOrDefault(i))
            })
            .ToList();
    }

    private static int? ToPercent(string? level, string? highLevel)
    {
        // Negative levels are IPP's "unavailable" (-1), "unknown" (-2) and "some remaining" (-3).
        if (!int.TryParse(level, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            return null;
        }

        return int.TryParse(highLevel, CultureInfo.InvariantCulture, out var high) && high > 0
            ? Math.Clamp(value * 100 / high, 0, 100)
            : Math.Clamp(value, 0, 100);
    }

    private static bool IsHpName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Contains("Hewlett", StringComparison.OrdinalIgnoreCase)
            || (trimmed.StartsWith("HP", StringComparison.OrdinalIgnoreCase) && (trimmed.Length == 2 || !char.IsLetter(trimmed[2])));
    }

    /// <summary>Parses an IEEE 1284 device ID (<c>MFG:HP;MDL:LaserJet M404;SN:...;</c>) into a key/value map.</summary>
    private static Dictionary<string, string> ParseDeviceId(string? deviceId)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (deviceId ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf(':');
            if (separator > 0)
            {
                var value = part[(separator + 1)..].Trim();
                if (value.Length > 0)
                {
                    result.TryAdd(part[..separator].Trim(), value);
                }
            }
        }

        return result;
    }

    private static string? StripUuidPrefix(string? uuid) =>
        uuid is not null && uuid.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase) ? uuid[9..] : uuid;

    private static bool? ParseBool(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" or "t" or "1" => true,
        "false" or "f" or "0" => false,
        _ => null
    };

    private static List<string> Values(IReadOnlyDictionary<string, List<string>> ipp, string name) =>
        ipp.TryGetValue(name, out var values) ? values : [];

    private static string? First(IReadOnlyDictionary<string, List<string>> ipp, string name) =>
        ipp.TryGetValue(name, out var values) ? values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) : null;

    private static string? Joined(IReadOnlyDictionary<string, List<string>> ipp, string name) =>
        ipp.TryGetValue(name, out var values) && values.Count > 0 ? string.Join(", ", values) : null;

    private static string? Txt(Dictionary<string, string> txt, string key) =>
        txt.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
