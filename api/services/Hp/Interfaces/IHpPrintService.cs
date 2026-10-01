using api.Data.Models;
using api.Services.Ipp.Dtos;

namespace api.Services.Hp.Interfaces;

public interface IHpPrintService
{
    /// <summary>Renders a test page for <paramref name="printer"/> and sends it as an IPP print job.</summary>
    Task<IppPrintJobResultDto> PrintTestPageAsync(HpPrinter printer, CancellationToken cancellationToken = default);
}
