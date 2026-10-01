using api.Data;
using api.Data.Models;
using api.Dtos.Hp;
using api.Services.Authentication;
using api.Services.Hp;
using api.Services.Hp.Interfaces;
using api.Services.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

/// <summary>
/// Discovery and registration of HP network printers. Master key only.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.MasterKeyOnly)]
[Route("[controller]/[action]")]
public class HpController(
    ApiDbContext dbContext,
    IHpPrinterDiscoveryService discoveryService,
    IHpPrintService printService,
    PrintJobFileStore printJobFileStore) : ControllerBase
{
    /// <summary>Searches the local network for HP printers (mDNS/Bonjour, optionally a subnet scan) and returns everything each one reports.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DiscoveredHpPrinterDto>>> Discover(
        [FromQuery] HpDiscoveryRequestDto request,
        CancellationToken cancellationToken)
    {
        var discovered = await discoveryService.DiscoverAsync(request, cancellationToken);

        var registered = await dbContext.HpPrinters
            .Select(p => new { p.Id, p.Uuid, p.Host })
            .ToListAsync(cancellationToken);

        foreach (var printer in discovered)
        {
            printer.RegisteredPrinterId = registered.FirstOrDefault(r =>
                (r.Uuid is not null && string.Equals(r.Uuid, printer.Uuid, StringComparison.OrdinalIgnoreCase))
                || string.Equals(r.Host, printer.Host, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        return Ok(discovered);
    }

    /// <summary>Queries a single printer by address without registering it — for printers that don't show up in Discover.</summary>
    [HttpGet]
    public async Task<ActionResult<HpPrinterInfoDto>> Inspect(
        [FromQuery] HpInspectRequestDto request,
        CancellationToken cancellationToken)
    {
        var info = await discoveryService.InspectAsync(request.Host, request.Port, request.ResourcePath, cancellationToken);
        return Ok(info);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<HpPrinterResponseDto>>> GetAll(
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.HpPrinters.AsQueryable();
        if (isActive.HasValue)
        {
            query = query.Where(p => p.IsActive == isActive.Value);
        }

        var printers = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return Ok(printers.Select(HpPrinterMapper.ToResponseDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HpPrinterResponseDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var printer = await dbContext.HpPrinters.FindAsync([id], cancellationToken);
        if (printer is null)
        {
            return NotFound();
        }

        return Ok(HpPrinterMapper.ToResponseDto(printer));
    }

    /// <summary>Queries the printer at the given address and stores everything it reports.</summary>
    [HttpPost]
    public async Task<ActionResult<HpPrinterResponseDto>> Register(
        RegisterHpPrinterRequestDto request,
        CancellationToken cancellationToken)
    {
        var info = await discoveryService.InspectAsync(request.Host, request.Port, request.ResourcePath, cancellationToken);

        if (!info.IsHp)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: $"The printer at {info.Host} reports itself as '{info.MakeAndModel ?? info.Manufacturer ?? "unknown"}', not an HP printer.");
        }

        var existing = await dbContext.HpPrinters.FirstOrDefaultAsync(
            p => (info.Uuid != null && p.Uuid == info.Uuid)
                || (p.Host == info.Host && p.Port == info.Port && p.ResourcePath == info.ResourcePath),
            cancellationToken);
        if (existing is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: $"This printer is already registered as '{existing.Name}' (id {existing.Id}).");
        }

        var printer = new HpPrinter
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? info.PrinterName ?? info.MakeAndModel ?? info.Host : request.Name,
            Note = request.Note
        };
        HpPrinterMapper.ApplyInfo(printer, info);

        dbContext.HpPrinters.Add(printer);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = printer.Id }, HpPrinterMapper.ToResponseDto(printer));
    }

    /// <summary>Updates the user-owned fields. If Host/Port/ResourcePath change, call Refresh afterwards to re-read the printer.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<HpPrinterResponseDto>> Update(
        int id,
        UpdateHpPrinterRequestDto request,
        CancellationToken cancellationToken)
    {
        var printer = await dbContext.HpPrinters.FindAsync([id], cancellationToken);
        if (printer is null)
        {
            return NotFound();
        }

        printer.Name = request.Name;
        printer.Note = request.Note;
        printer.IsActive = request.IsActive;
        printer.Host = request.Host.Trim();
        printer.Port = request.Port;
        printer.ResourcePath = HpPrinterInfoBuilder.NormalizeResourcePath(request.ResourcePath);
        printer.PrinterUri = HpPrinterInfoBuilder.BuildPrinterUri(printer.Host, printer.Port, printer.ResourcePath).ToString();
        printer.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(HpPrinterMapper.ToResponseDto(printer));
    }

    /// <summary>Re-queries the printer and updates all stored information (state, supplies, firmware, ...).</summary>
    [HttpPost("{id:int}")]
    public async Task<ActionResult<HpPrinterResponseDto>> Refresh(int id, CancellationToken cancellationToken)
    {
        var printer = await dbContext.HpPrinters.FindAsync([id], cancellationToken);
        if (printer is null)
        {
            return NotFound();
        }

        var info = await discoveryService.InspectAsync(printer.Host, printer.Port, printer.ResourcePath, cancellationToken);

        if (printer.Uuid is not null && info.Uuid is not null && !string.Equals(printer.Uuid, info.Uuid, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: $"A different printer (uuid {info.Uuid}, '{info.MakeAndModel}') now answers at {printer.Host}. Update the host of '{printer.Name}' first.");
        }

        HpPrinterMapper.ApplyInfo(printer, info);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(HpPrinterMapper.ToResponseDto(printer));
    }

    /// <summary>Prints a test page (printer details, color and gray patches, line weights, supply levels) on the printer.</summary>
    [HttpPost("{id:int}")]
    public async Task<ActionResult<HpPrintJobResponseDto>> PrintTestPage(int id, CancellationToken cancellationToken)
    {
        var printer = await dbContext.HpPrinters.FindAsync([id], cancellationToken);
        if (printer is null)
        {
            return NotFound();
        }

        if (!printer.IsActive)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: $"Printer '{printer.Name}' is deactivated. Activate it with Update before printing.");
        }

        if (printer.DocumentFormats.Count > 0
            && !printer.DocumentFormats.Contains(HpTestPageRenderer.DocumentFormat, StringComparer.OrdinalIgnoreCase))
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: $"Printer '{printer.Name}' doesn't accept {HpTestPageRenderer.DocumentFormat}; it supports {string.Join(", ", printer.DocumentFormats)}.");
        }

        var result = await printService.PrintTestPageAsync(printer, cancellationToken);

        return Ok(new HpPrintJobResponseDto
        {
            PrinterId = printer.Id,
            PrinterName = printer.Name,
            JobId = result.JobId,
            JobUri = result.JobUri,
            JobState = result.JobState,
            JobStateReasons = result.JobStateReasons,
            IppStatusCode = result.IppStatusCode,
            IppStatusMessage = result.IppStatusMessage
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var printer = await dbContext.HpPrinters.FindAsync([id], cancellationToken);
        if (printer is null)
        {
            return NotFound();
        }

        var hasUnfinishedJobs = await dbContext.PrintJobs.AnyAsync(
            j => j.PrinterId == id
                && (j.Status == PrintJobStatus.Queued || j.Status == PrintJobStatus.Processing || j.Status == PrintJobStatus.Printing),
            cancellationToken);
        if (hasUnfinishedJobs)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: $"Printer '{printer.Name}' has unfinished print jobs. Cancel them (POST /Print/Cancel/{{id}}) before deleting it.");
        }

        // The printer's job history is removed by cascade delete.
        var jobIds = await dbContext.PrintJobs.Where(j => j.PrinterId == id).Select(j => j.Id).ToListAsync(cancellationToken);

        dbContext.HpPrinters.Remove(printer);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var jobId in jobIds)
        {
            printJobFileStore.Delete(jobId);
        }

        return NoContent();
    }
}
