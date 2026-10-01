using System.Text.Json;
using api.Data.Models;
using api.Dtos.Hp;

namespace api.Services.Hp;

public static class HpPrinterMapper
{
    /// <summary>Copies everything retrieved from the printer onto the entity, leaving user-owned fields (Name, Note, IsActive) alone.</summary>
    public static void ApplyInfo(HpPrinter entity, HpPrinterInfoDto info)
    {
        entity.Host = info.Host;
        entity.IpAddress = info.IpAddress;
        entity.Port = info.Port;
        entity.ResourcePath = info.ResourcePath;
        entity.PrinterUri = info.PrinterUri;
        entity.PrinterName = info.PrinterName;
        entity.Manufacturer = info.Manufacturer;
        entity.Model = info.Model;
        entity.MakeAndModel = info.MakeAndModel;
        entity.SerialNumber = info.SerialNumber;
        entity.Uuid = info.Uuid;
        entity.DeviceId = info.DeviceId;
        entity.FirmwareVersion = info.FirmwareVersion;
        entity.Location = info.Location;
        entity.Info = info.Info;
        entity.AdminUrl = info.AdminUrl;
        entity.State = info.State;
        entity.StateReasons = info.StateReasons;
        entity.StateMessage = info.StateMessage;
        entity.SupportsColor = info.SupportsColor;
        entity.SupportsDuplex = info.SupportsDuplex;
        entity.DocumentFormats = info.DocumentFormats;
        entity.MediaSupported = info.MediaSupported;
        entity.MediaDefault = info.MediaDefault;
        entity.Resolutions = info.Resolutions;
        entity.IppVersions = info.IppVersions;
        entity.IppAttributesJson = JsonSerializer.Serialize(info.IppAttributes);
        entity.InfoRetrievedAt = info.InfoRetrievedAt;
        entity.UpdatedAt = DateTime.UtcNow;

        // mDNS answers are best-effort; keep what we had if the printer didn't answer this time.
        if (info.MdnsTxtRecords.Count > 0 || info.MdnsServices.Count > 0)
        {
            entity.MdnsHostName = info.MdnsHostName;
            entity.MdnsServices = info.MdnsServices;
            entity.MdnsTxtRecordsJson = JsonSerializer.Serialize(info.MdnsTxtRecords);
        }
    }

    public static HpPrinterResponseDto ToResponseDto(HpPrinter entity)
    {
        var ippAttributes = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(entity.IppAttributesJson) ?? [];

        return new HpPrinterResponseDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Note = entity.Note,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Host = entity.Host,
            IpAddress = entity.IpAddress,
            MdnsHostName = entity.MdnsHostName,
            Port = entity.Port,
            ResourcePath = entity.ResourcePath,
            PrinterUri = entity.PrinterUri,
            PrinterName = entity.PrinterName,
            Manufacturer = entity.Manufacturer,
            Model = entity.Model,
            MakeAndModel = entity.MakeAndModel,
            SerialNumber = entity.SerialNumber,
            Uuid = entity.Uuid,
            DeviceId = entity.DeviceId,
            FirmwareVersion = entity.FirmwareVersion,
            Location = entity.Location,
            Info = entity.Info,
            AdminUrl = entity.AdminUrl,
            State = entity.State,
            StateReasons = entity.StateReasons,
            StateMessage = entity.StateMessage,
            SupportsColor = entity.SupportsColor,
            SupportsDuplex = entity.SupportsDuplex,
            DocumentFormats = entity.DocumentFormats,
            MediaSupported = entity.MediaSupported,
            MediaDefault = entity.MediaDefault,
            Resolutions = entity.Resolutions,
            IppVersions = entity.IppVersions,
            Supplies = HpPrinterInfoBuilder.ParseSupplies(ippAttributes),
            MdnsServices = entity.MdnsServices,
            IsHp = HpPrinterInfoBuilder.IsHpManufacturer(entity.Manufacturer, entity.MakeAndModel),
            IppAttributes = ippAttributes,
            MdnsTxtRecords = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.MdnsTxtRecordsJson) ?? [],
            InfoRetrievedAt = entity.InfoRetrievedAt
        };
    }
}
