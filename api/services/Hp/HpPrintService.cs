using api.Data.Models;
using api.Services.Hp.Interfaces;
using api.Services.Ipp.Dtos;
using api.Services.Ipp.Interfaces;

namespace api.Services.Hp;

public class HpPrintService(
    IIppClient ippClient,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<HpPrintService> logger) : IHpPrintService
{
    public async Task<IppPrintJobResultDto> PrintTestPageAsync(HpPrinter printer, CancellationToken cancellationToken = default)
    {
        var apiName = configuration["Api:Name"] ?? "api";
        using var document = new MemoryStream(HpTestPageRenderer.RenderJpeg(printer, apiName, environment.EnvironmentName));

        var result = await ippClient.PrintJobAsync(
            new Uri(printer.PrinterUri),
            new IppPrintJobRequestDto
            {
                Document = document,
                DocumentFormat = HpTestPageRenderer.DocumentFormat,
                JobName = $"{apiName} test page",
                RequestingUserName = apiName,
                PrintColorMode = "color",
                PrintScaling = "fit"
            },
            cancellationToken);

        logger.LogInformation(
            "Test page sent to printer {PrinterId} ({PrinterUri}): job {JobId}, state {JobState}, IPP status {IppStatus}",
            printer.Id, printer.PrinterUri, result.JobId, result.JobState, result.IppStatusCode);

        return result;
    }
}
