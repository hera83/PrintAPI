using System.Net;

namespace api.Services.Ipp;

public class IppException : Exception
{
    public HttpStatusCode StatusCode { get; }

    /// <summary>The IPP status code when the printer answered with an IPP error; null for network/HTTP/timeout failures.</summary>
    public ushort? IppStatusCode { get; }

    /// <summary>True when the printer never answered (connection failed or timed out), as opposed to answering with an error.</summary>
    public bool IsUnreachable { get; }

    public IppException(HttpStatusCode statusCode, string message, ushort? ippStatusCode = null, bool isUnreachable = false)
        : base(message)
    {
        StatusCode = statusCode;
        IppStatusCode = ippStatusCode;
        IsUnreachable = isUnreachable;
    }

    /// <summary>
    /// True when retrying later may succeed: network errors, timeouts and server-error-* statuses (busy, not accepting jobs, ...),
    /// but not client-error-* (bad request, unsupported document format, ...) or operation/version-not-supported.
    /// </summary>
    public bool IsTransient => IppStatusCode is null or (>= 0x0500 and not 0x0501 and not 0x0502);
}
