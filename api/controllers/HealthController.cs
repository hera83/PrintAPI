using System.Net;
using api.Data;
using api.Data.Models;
using api.Dtos.Health;
using api.Services.Ipp;
using api.Services.Ipp.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

/// <summary>
/// Reports whether the API can actually print: every active registered printer is queried (in parallel, with a short
/// timeout) and the API counts as healthy only if at least one of them is online. Unhealthy answers 503 so
/// monitoring/load balancers pick it up from the status code alone.
/// </summary>
[ApiController]
[Route("[controller]")]
[AllowAnonymous]
public class HealthController(ApiDbContext dbContext, IIppClient ippClient) : ControllerBase
{
    private static readonly TimeSpan PrinterTimeout = TimeSpan.FromSeconds(3);

    [HttpGet]
    [ProducesResponseType<HealthResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponseDto>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponseDto>> Get(CancellationToken cancellationToken)
    {
        var printers = await dbContext.HpPrinters
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var results = await Task.WhenAll(printers.Select(p => CheckPrinterAsync(p, cancellationToken)));
        var online = results.Count(r => r.IsOnline);

        var response = new HealthResponseDto
        {
            Status = online > 0 ? "Healthy" : "Unhealthy",
            TimestampUtc = DateTime.UtcNow,
            PrintersOnline = online,
            PrintersTotal = results.Length,
            Printers = [.. results]
        };

        return online > 0 ? Ok(response) : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    private async Task<PrinterHealthDto> CheckPrinterAsync(HpPrinter printer, CancellationToken cancellationToken)
    {
        var result = new PrinterHealthDto { Id = printer.Id, Name = printer.Name };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PrinterTimeout);

        try
        {
            var status = await ippClient.GetPrinterStatusAsync(new Uri(printer.PrinterUri), timeoutCts.Token);
            result.IsOnline = true;
            result.State = status.State;
            result.StateReasons = status.StateReasons;
            result.IsAcceptingJobs = status.IsAcceptingJobs;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.Error = $"No answer within {PrinterTimeout.TotalSeconds:0} s.";
        }
        catch (IppException ex) when (ex.StatusCode == HttpStatusCode.GatewayTimeout)
        {
            result.Error = $"No answer within {PrinterTimeout.TotalSeconds:0} s.";
        }
        catch (IppException)
        {
            // The exception message contains the printer's address, which an anonymous endpoint shouldn't reveal.
            result.Error = "Not reachable, or rejected the status query.";
        }

        return result;
    }
}
