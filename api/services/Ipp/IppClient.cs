using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using api.Services.Ipp.Dtos;
using api.Services.Ipp.Interfaces;

namespace api.Services.Ipp;

/// <summary>
/// Minimal IPP/2.0 client (RFC 8010/8011) over plain HTTP — encodes requests and decodes the binary
/// attribute stream by hand, so no third-party IPP library is needed.
/// </summary>
public class IppClient(HttpClient httpClient) : IIppClient
{
    private const ushort PrintJobOperation = 0x0002;
    private const ushort CancelJobOperation = 0x0008;
    private const ushort GetJobAttributesOperation = 0x0009;
    private const ushort GetPrinterAttributesOperation = 0x000B;

    private const byte OperationAttributesTag = 0x01;
    private const byte JobAttributesTag = 0x02;
    private const byte EndOfAttributesTag = 0x03;
    private const byte PrinterAttributesTag = 0x04;

    private const byte UnsupportedTag = 0x10;
    private const byte UnknownTag = 0x12;
    private const byte NoValueTag = 0x13;
    private const byte IntegerTag = 0x21;
    private const byte BooleanTag = 0x22;
    private const byte EnumTag = 0x23;
    private const byte OctetStringTag = 0x30;
    private const byte DateTimeTag = 0x31;
    private const byte ResolutionTag = 0x32;
    private const byte RangeOfIntegerTag = 0x33;
    private const byte BeginCollectionTag = 0x34;
    private const byte TextWithLanguageTag = 0x35;
    private const byte NameWithLanguageTag = 0x36;
    private const byte EndCollectionTag = 0x37;
    private const byte NameWithoutLanguageTag = 0x42;
    private const byte KeywordTag = 0x44;
    private const byte UriTag = 0x45;
    private const byte CharsetTag = 0x47;
    private const byte NaturalLanguageTag = 0x48;
    private const byte MimeMediaTypeTag = 0x49;
    private const byte MemberAttrNameTag = 0x4A;

    // The typed HttpClient has no timeout of its own; each operation applies its own.
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PrintJobTimeout = TimeSpan.FromMinutes(5);

    private static readonly Dictionary<ushort, string> StatusCodeNames = new()
    {
        [0x0400] = "client-error-bad-request",
        [0x0401] = "client-error-forbidden",
        [0x0402] = "client-error-not-authenticated",
        [0x0403] = "client-error-not-authorized",
        [0x0404] = "client-error-not-possible",
        [0x0405] = "client-error-timeout",
        [0x0406] = "client-error-not-found",
        [0x0408] = "client-error-request-entity-too-large",
        [0x040A] = "client-error-document-format-not-supported",
        [0x040B] = "client-error-attributes-or-values-not-supported",
        [0x0500] = "server-error-internal-error",
        [0x0501] = "server-error-operation-not-supported",
        [0x0506] = "server-error-not-accepting-jobs",
        [0x0507] = "server-error-busy"
    };

    private static readonly Dictionary<string, string> JobStateNames = new()
    {
        ["3"] = "pending",
        ["4"] = "pending-held",
        ["5"] = "processing",
        ["6"] = "processing-stopped",
        ["7"] = "canceled",
        ["8"] = "aborted",
        ["9"] = "completed"
    };

    public async Task<Dictionary<string, List<string>>> GetPrinterAttributesAsync(
        Uri printerUri,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        WriteHeader(stream, GetPrinterAttributesOperation);
        WriteOperationAttributes(stream, printerUri, "PrintAPI");
        WriteAttribute(stream, KeywordTag, "requested-attributes", "all");
        stream.WriteByte(EndOfAttributesTag);

        var response = await SendAsync(printerUri, stream.ToArray(), null, QueryTimeout, "Get-Printer-Attributes", cancellationToken);
        return response.Group(PrinterAttributesTag);
    }

    public async Task<IppPrinterStatusDto> GetPrinterStatusAsync(
        Uri printerUri,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        WriteHeader(stream, GetPrinterAttributesOperation);
        WriteOperationAttributes(stream, printerUri, "PrintAPI");
        WriteAttribute(stream, KeywordTag, "requested-attributes", "printer-state");
        WriteAdditionalValue(stream, KeywordTag, "printer-state-reasons");
        WriteAdditionalValue(stream, KeywordTag, "printer-state-message");
        WriteAdditionalValue(stream, KeywordTag, "printer-is-accepting-jobs");
        stream.WriteByte(EndOfAttributesTag);

        var response = await SendAsync(printerUri, stream.ToArray(), null, QueryTimeout, "Get-Printer-Attributes", cancellationToken);
        var printer = response.Group(PrinterAttributesTag);

        return new IppPrinterStatusDto
        {
            State = printer.GetValueOrDefault("printer-state")?.FirstOrDefault() switch
            {
                "3" => "idle",
                "4" => "processing",
                "5" => "stopped",
                var other => other
            },
            StateReasons = printer.GetValueOrDefault("printer-state-reasons") ?? [],
            StateMessage = printer.GetValueOrDefault("printer-state-message")?.FirstOrDefault(),
            IsAcceptingJobs = bool.TryParse(printer.GetValueOrDefault("printer-is-accepting-jobs")?.FirstOrDefault(), out var accepting)
                ? accepting
                : null
        };
    }

    public async Task<IppJobStatusDto> GetJobAttributesAsync(
        Uri printerUri,
        int jobId,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        WriteHeader(stream, GetJobAttributesOperation);
        WriteOperationAttributes(stream, printerUri, "PrintAPI");
        WriteIntegerAttribute(stream, IntegerTag, "job-id", jobId);
        WriteAttribute(stream, KeywordTag, "requested-attributes", "job-state");
        WriteAdditionalValue(stream, KeywordTag, "job-state-reasons");
        WriteAdditionalValue(stream, KeywordTag, "job-state-message");
        WriteAdditionalValue(stream, KeywordTag, "job-impressions-completed");
        stream.WriteByte(EndOfAttributesTag);

        var response = await SendAsync(printerUri, stream.ToArray(), null, QueryTimeout, "Get-Job-Attributes", cancellationToken);
        var job = response.Group(JobAttributesTag);
        var jobState = job.GetValueOrDefault("job-state")?.FirstOrDefault();

        return new IppJobStatusDto
        {
            JobState = jobState is not null ? JobStateNames.GetValueOrDefault(jobState, jobState) : null,
            JobStateReasons = job.GetValueOrDefault("job-state-reasons") ?? [],
            JobStateMessage = job.GetValueOrDefault("job-state-message")?.FirstOrDefault(),
            ImpressionsCompleted = int.TryParse(job.GetValueOrDefault("job-impressions-completed")?.FirstOrDefault(), CultureInfo.InvariantCulture, out var impressions)
                ? impressions
                : null
        };
    }

    public async Task CancelJobAsync(Uri printerUri, int jobId, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        WriteHeader(stream, CancelJobOperation);
        WriteOperationAttributes(stream, printerUri, "PrintAPI");
        WriteIntegerAttribute(stream, IntegerTag, "job-id", jobId);
        stream.WriteByte(EndOfAttributesTag);

        await SendAsync(printerUri, stream.ToArray(), null, QueryTimeout, "Cancel-Job", cancellationToken);
    }

    public async Task<IppPrintJobResultDto> PrintJobAsync(
        Uri printerUri,
        IppPrintJobRequestDto request,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        WriteHeader(stream, PrintJobOperation);
        WriteOperationAttributes(stream, printerUri, request.RequestingUserName);
        WriteAttribute(stream, NameWithoutLanguageTag, "job-name", request.JobName);
        WriteAttribute(stream, MimeMediaTypeTag, "document-format", request.DocumentFormat);

        var jobKeywords = new (string Name, string? Value)[]
        {
            ("media", request.Media),
            ("print-color-mode", request.PrintColorMode),
            ("print-scaling", request.PrintScaling),
            ("sides", request.Sides)
        }.Where(k => !string.IsNullOrWhiteSpace(k.Value)).ToList();

        if (request.Copies.HasValue || request.PrintQuality.HasValue || request.PageRanges is { Count: > 0 } || jobKeywords.Count > 0)
        {
            stream.WriteByte(JobAttributesTag);
            if (request.Copies is { } copies)
            {
                WriteIntegerAttribute(stream, IntegerTag, "copies", copies);
            }

            if (request.PrintQuality is { } printQuality)
            {
                WriteIntegerAttribute(stream, EnumTag, "print-quality", printQuality);
            }

            foreach (var (name, value) in jobKeywords)
            {
                WriteAttribute(stream, KeywordTag, name, value!);
            }

            if (request.PageRanges is { Count: > 0 } pageRanges)
            {
                for (var i = 0; i < pageRanges.Count; i++)
                {
                    WriteRangeAttribute(stream, i == 0 ? "page-ranges" : string.Empty, pageRanges[i].From, pageRanges[i].To);
                }
            }
        }

        stream.WriteByte(EndOfAttributesTag);

        var response = await SendAsync(printerUri, stream.ToArray(), request.Document, request.Timeout ?? PrintJobTimeout, "Print-Job", cancellationToken);
        var job = response.Group(JobAttributesTag);
        var jobState = job.GetValueOrDefault("job-state")?.FirstOrDefault();

        return new IppPrintJobResultDto
        {
            JobId = int.TryParse(job.GetValueOrDefault("job-id")?.FirstOrDefault(), CultureInfo.InvariantCulture, out var jobId) ? jobId : null,
            JobUri = job.GetValueOrDefault("job-uri")?.FirstOrDefault(),
            JobState = jobState is not null ? JobStateNames.GetValueOrDefault(jobState, jobState) : null,
            JobStateReasons = job.GetValueOrDefault("job-state-reasons") ?? [],
            IppStatusCode = $"0x{response.StatusCode:X4}",
            IppStatusMessage = response.StatusMessage
        };
    }

    private async Task<IppResponse> SendAsync(
        Uri printerUri,
        byte[] attributes,
        Stream? document,
        TimeSpan timeout,
        string operationName,
        CancellationToken cancellationToken)
    {
        var httpUri = new UriBuilder(printerUri) { Scheme = Uri.UriSchemeHttp }.Uri;

        using HttpContent content = document is null ? new ByteArrayContent(attributes) : new IppRequestContent(attributes, document);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        byte[] responseBody;
        try
        {
            using var response = await httpClient.PostAsync(httpUri, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new IppException(
                    HttpStatusCode.BadGateway,
                    $"Printer at {httpUri} answered HTTP {(int)response.StatusCode} {response.ReasonPhrase} to the IPP {operationName} request.");
            }

            responseBody = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
        }
        catch (HttpRequestException ex)
        {
            throw new IppException(HttpStatusCode.BadGateway, $"Could not reach printer at {httpUri}: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IppException(HttpStatusCode.GatewayTimeout, $"Timed out waiting for printer at {httpUri}.");
        }

        var ippResponse = ParseResponse(responseBody, httpUri);

        // 0x0000-0x00FF are the successful-ok* status codes.
        if (ippResponse.StatusCode >= 0x0100)
        {
            var name = StatusCodeNames.TryGetValue(ippResponse.StatusCode, out var statusName) ? $" ({statusName})" : string.Empty;
            var suffix = ippResponse.StatusMessage is null ? string.Empty : $": {ippResponse.StatusMessage}";
            var statusCode = ippResponse.StatusCode is 0x0506 or 0x0507 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadGateway;
            throw new IppException(
                statusCode,
                $"Printer at {httpUri} rejected {operationName} with IPP status 0x{ippResponse.StatusCode:X4}{name}{suffix}.",
                ippResponse.StatusCode);
        }

        return ippResponse;
    }

    private static void WriteHeader(Stream stream, ushort operation)
    {
        stream.WriteByte(2); // version 2.0
        stream.WriteByte(0);
        WriteUInt16(stream, operation);
        WriteInt32(stream, Random.Shared.Next(1, int.MaxValue)); // request-id
    }

    private static void WriteOperationAttributes(Stream stream, Uri printerUri, string requestingUserName)
    {
        stream.WriteByte(OperationAttributesTag);
        WriteAttribute(stream, CharsetTag, "attributes-charset", "utf-8");
        WriteAttribute(stream, NaturalLanguageTag, "attributes-natural-language", "en");
        WriteAttribute(stream, UriTag, "printer-uri", printerUri.ToString());
        WriteAttribute(stream, NameWithoutLanguageTag, "requesting-user-name", requestingUserName);
    }

    private static void WriteAttribute(Stream stream, byte tag, string name, string value)
    {
        stream.WriteByte(tag);
        WriteString(stream, name);
        WriteString(stream, value);
    }

    /// <summary>Writes another value of the previous attribute (1setOf) — an attribute with a zero-length name.</summary>
    private static void WriteAdditionalValue(Stream stream, byte tag, string value)
    {
        stream.WriteByte(tag);
        WriteUInt16(stream, 0);
        WriteString(stream, value);
    }

    /// <summary>Writes a rangeOfInteger value; an empty name adds another value to the previous attribute.</summary>
    private static void WriteRangeAttribute(Stream stream, string name, int lower, int upper)
    {
        stream.WriteByte(RangeOfIntegerTag);
        WriteString(stream, name);
        WriteUInt16(stream, 8);
        WriteInt32(stream, lower);
        WriteInt32(stream, upper);
    }

    private static void WriteIntegerAttribute(Stream stream, byte tag, string name, int value)
    {
        stream.WriteByte(tag);
        WriteString(stream, name);
        WriteUInt16(stream, 4);
        WriteInt32(stream, value);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt16(stream, (ushort)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static IppResponse ParseResponse(byte[] data, Uri httpUri)
    {
        try
        {
            var reader = new IppReader(data);
            reader.ReadUInt16(); // version
            var statusCode = reader.ReadUInt16();
            reader.ReadInt32(); // request-id

            var groups = new Dictionary<byte, Dictionary<string, List<string>>>();
            Dictionary<string, List<string>>? currentGroup = null;
            string? currentName = null;

            while (reader.HasMore)
            {
                var tag = reader.ReadByte();
                if (tag == EndOfAttributesTag)
                {
                    break;
                }

                if (tag < 0x10)
                {
                    if (!groups.TryGetValue(tag, out currentGroup))
                    {
                        currentGroup = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                        groups[tag] = currentGroup;
                    }

                    currentName = null;
                    continue;
                }

                var name = reader.ReadString(reader.ReadUInt16());
                var value = ReadValue(reader, tag);

                // A zero-length name means "additional value of the previous attribute" (1setOf).
                if (name.Length > 0)
                {
                    currentName = name;
                }

                if (currentGroup is null || currentName is null)
                {
                    continue;
                }

                if (!currentGroup.TryGetValue(currentName, out var values))
                {
                    values = [];
                    currentGroup[currentName] = values;
                }

                values.Add(value);
            }

            var response = new IppResponse(statusCode, groups);
            return response with { StatusMessage = response.Group(OperationAttributesTag).GetValueOrDefault("status-message")?.FirstOrDefault() };
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException)
        {
            throw new IppException(HttpStatusCode.BadGateway, $"Printer at {httpUri} returned a malformed IPP response.");
        }
    }

    private static string ReadValue(IppReader reader, byte tag)
    {
        var valueBytes = reader.ReadBytes(reader.ReadUInt16());
        return tag == BeginCollectionTag ? ReadCollection(reader) : DecodeValue(tag, valueBytes);
    }

    /// <summary>Renders a collection as <c>{member=value member=value}</c>, the same notation CUPS' ipptool uses.</summary>
    private static string ReadCollection(IppReader reader)
    {
        var members = new List<string>();
        string? memberName = null;

        while (true)
        {
            var tag = reader.ReadByte();
            reader.ReadBytes(reader.ReadUInt16()); // name is always empty inside a collection

            if (tag == EndCollectionTag)
            {
                reader.ReadBytes(reader.ReadUInt16());
                break;
            }

            if (tag == MemberAttrNameTag)
            {
                memberName = reader.ReadString(reader.ReadUInt16());
                continue;
            }

            members.Add($"{memberName}={ReadValue(reader, tag)}");
        }

        return "{" + string.Join(' ', members) + "}";
    }

    private static string DecodeValue(byte tag, byte[] value)
    {
        switch (tag)
        {
            case UnsupportedTag:
                return "unsupported";
            case UnknownTag:
                return "unknown";
            case NoValueTag:
                return "no-value";
            case IntegerTag or EnumTag when value.Length == 4:
                return BinaryPrimitives.ReadInt32BigEndian(value).ToString(CultureInfo.InvariantCulture);
            case BooleanTag when value.Length == 1:
                return value[0] != 0 ? "true" : "false";
            case DateTimeTag when value.Length == 11:
                var year = BinaryPrimitives.ReadUInt16BigEndian(value);
                return $"{year:D4}-{value[2]:D2}-{value[3]:D2}T{value[4]:D2}:{value[5]:D2}:{value[6]:D2}{(char)value[8]}{value[9]:D2}:{value[10]:D2}";
            case ResolutionTag when value.Length == 9:
                var x = BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(0, 4));
                var y = BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(4, 4));
                return $"{x}x{y}{(value[8] == 4 ? "dpcm" : "dpi")}";
            case RangeOfIntegerTag when value.Length == 8:
                var lower = BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(0, 4));
                var upper = BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(4, 4));
                return $"{lower}-{upper}";
            case TextWithLanguageTag or NameWithLanguageTag:
                var languageLength = BinaryPrimitives.ReadUInt16BigEndian(value);
                var textLength = BinaryPrimitives.ReadUInt16BigEndian(value.AsSpan(2 + languageLength));
                return Encoding.UTF8.GetString(value, 4 + languageLength, textLength);
            case OctetStringTag:
                var text = Encoding.UTF8.GetString(value);
                return text.Any(char.IsControl) ? Convert.ToHexString(value) : text;
            case >= 0x40 and <= 0x5F:
                return Encoding.UTF8.GetString(value);
            default:
                return Convert.ToHexString(value);
        }
    }

    /// <summary>Request body = encoded attributes followed by the document, streamed without buffering the document in memory.</summary>
    private sealed class IppRequestContent(byte[] attributes, Stream document) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(attributes, cancellationToken);
            await document.CopyToAsync(stream, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = document.CanSeek ? attributes.Length + document.Length - document.Position : 0;
            return document.CanSeek;
        }
    }

    private sealed record IppResponse(ushort StatusCode, Dictionary<byte, Dictionary<string, List<string>>> Groups)
    {
        public string? StatusMessage { get; init; }

        public Dictionary<string, List<string>> Group(byte tag) =>
            Groups.GetValueOrDefault(tag) ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
    }

    private sealed class IppReader(byte[] data)
    {
        private int _position;

        public bool HasMore => _position < data.Length;

        public byte ReadByte() => data[_position++];

        public ushort ReadUInt16()
        {
            var value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(_position, 2));
            _position += 2;
            return value;
        }

        public int ReadInt32()
        {
            var value = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(_position, 4));
            _position += 4;
            return value;
        }

        public byte[] ReadBytes(int count)
        {
            var value = data.AsSpan(_position, count).ToArray();
            _position += count;
            return value;
        }

        public string ReadString(int count) => Encoding.UTF8.GetString(ReadBytes(count));
    }
}
