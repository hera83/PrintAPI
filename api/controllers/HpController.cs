using api.Data;
using api.Data.Models;
using api.Dtos.Hp;
using api.Services.Authentication;
using api.Services.Hp;
using api.Services.Hp.Interfaces;
using api.Services.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
    HpDiscoveryJobStore discoveryJobStore,
    PrintJobFileStore printJobFileStore) : ControllerBase
{
    /// <summary>
    /// Starts a search of the local network for HP printers (mDNS/Bonjour, optionally a subnet scan) in the background
    /// and returns at once. Follow it with <c>GET /Hp/GetDiscover/{id}</c> — a subnet scan of a /16 takes about 2 minutes.
    /// The body is optional; send <c>{ "scanSubnet": true }</c> to also scan the server's own subnet(s).
    /// </summary>
    [HttpPost]
    public ActionResult<HpDiscoveryJobResponseDto> StartDiscover(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] HpDiscoveryRequestDto? request)
    {
        if (!discoveryJobStore.TryEnqueue(request ?? new HpDiscoveryRequestDto(), out var job))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: $"A printer discovery is already running (id {job.Id}). Follow it with GET /Hp/GetDiscover/{job.Id}.");
        }

        return AcceptedAtAction(nameof(GetDiscover), new { id = job.Id }, ToDiscoveryResponseDto(job, job.Status));
    }

    /// <summary>Progress of a discovery started with <c>StartDiscover</c>, and the printers found once it's completed. Results are kept for an hour.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<HpDiscoveryJobResponseDto>> GetDiscover(Guid id, CancellationToken cancellationToken)
    {
        var job = discoveryJobStore.Get(id);
        if (job is null)
        {
            return NotFound();
        }

        // Read the status before the results: the worker writes the results first, then the status.
        var status = job.Status;
        var response = ToDiscoveryResponseDto(job, status);
        if (status != HpDiscoveryJobStatus.Completed)
        {
            return Ok(response);
        }

        var registered = await dbContext.HpPrinters
            .Select(p => new { p.Id, p.Uuid, p.Host })
            .ToListAsync(cancellationToken);

        foreach (var printer in response.Printers)
        {
            printer.RegisteredPrinterId = registered.FirstOrDefault(r =>
                (r.Uuid is not null && string.Equals(r.Uuid, printer.Uuid, StringComparison.OrdinalIgnoreCase))
                || string.Equals(r.Host, printer.Host, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        return Ok(response);
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

    /// <summary>
    /// Registers the printer at the given IP address or hostname. Only <c>host</c> is required: the API reads everything
    /// else (name, model, serial number, capabilities, supplies, ...) from the printer itself.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<HpPrinterResponseDto>> Register(
        RegisterHpPrinterRequestDto request,
        CancellationToken cancellationToken)
    {
        var info = await discoveryService.InspectAsync(request.Host, cancellationToken: cancellationToken);

        if (!info.IsHp)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: $"The printer at {info.Host} reports itself as '{info.MakeAndModel ?? info.Manufacturer ?? "unknown"}', not an HP printer.");
        }

        var existing = await dbContext.HpPrinters.FirstOrDefaultAsync(
            p => (info.Uuid != null && p.Uuid == info.Uuid) || p.Host == info.Host,
            cancellationToken);
        if (existing is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: $"This printer is already registered as '{existing.Name}' (id {existing.Id}).");
        }

        var printer = new HpPrinter
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? info.PrinterName ?? info.MakeAndModel ?? info.Host : request.Name.Trim(),
            Note = request.Note
        };
        HpPrinterMapper.ApplyInfo(printer, info);

        dbContext.HpPrinters.Add(printer);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = printer.Id }, HpPrinterMapper.ToResponseDto(printer));
    }

    /// <summary>
    /// Changes only the fields you send. If <c>host</c> changes (the printer got a new IP address), the printer is read
    /// again at the new address, and it must be the same printer.
    /// </summary>
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

        var newHost = request.Host?.Trim();
        if (!string.IsNullOrEmpty(newHost) && !string.Equals(newHost, printer.Host, StringComparison.OrdinalIgnoreCase))
        {
            var takenBy = await dbContext.HpPrinters.FirstOrDefaultAsync(p => p.Id != id && p.Host == newHost, cancellationToken);
            if (takenBy is not null)
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    detail: $"{newHost} is already registered as '{takenBy.Name}' (id {takenBy.Id}).");
            }

            var problem = await ReadPrinterAsync(printer, newHost, cancellationToken);
            if (problem is not null)
            {
                return problem;
            }
        }

        if (request.Name is not null)
        {
            printer.Name = request.Name.Trim();
        }

        if (request.Note is not null)
        {
            printer.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note;
        }

        if (request.IsActive is not null)
        {
            printer.IsActive = request.IsActive.Value;
        }

        printer.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(HpPrinterMapper.ToResponseDto(printer));
    }

    /// <summary>Reads the printer again and updates everything stored about it (state, supplies, firmware, ...).</summary>
    [HttpPost("{id:int}")]
    public async Task<ActionResult<HpPrinterResponseDto>> Refresh(int id, CancellationToken cancellationToken)
    {
        var printer = await dbContext.HpPrinters.FindAsync([id], cancellationToken);
        if (printer is null)
        {
            return NotFound();
        }

        var problem = await ReadPrinterAsync(printer, printer.Host, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

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

    /// <summary>
    /// Reads the printer at <paramref name="host"/> and copies everything onto <paramref name="printer"/> (not saved), or
    /// returns a 409 if a different printer answers there.
    /// </summary>
    private async Task<ObjectResult?> ReadPrinterAsync(HpPrinter printer, string host, CancellationToken cancellationToken)
    {
        var info = await discoveryService.InspectAsync(host, printer.ResourcePath, cancellationToken);

        if (printer.Uuid is not null && info.Uuid is not null && !string.Equals(printer.Uuid, info.Uuid, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: $"A different printer ('{info.MakeAndModel}', uuid {info.Uuid}) answers at {host}, not '{printer.Name}'. Register it as a new printer, or update '{printer.Name}' with its new address.");
        }

        HpPrinterMapper.ApplyInfo(printer, info);
        return null;
    }

    private static HpDiscoveryJobResponseDto ToDiscoveryResponseDto(HpDiscoveryJob job, HpDiscoveryJobStatus status) => new()
    {
        Id = job.Id,
        Status = status.ToString(),
        IsFinished = status is HpDiscoveryJobStatus.Completed or HpDiscoveryJobStatus.Failed,
        Phase = job.Progress.Phase,
        HostsToProbe = job.Progress.HostsToProbe,
        HostsProbed = job.Progress.HostsProbed,
        DevicesToInspect = job.Progress.DevicesToInspect,
        DevicesInspected = job.Progress.DevicesInspected,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        Error = status == HpDiscoveryJobStatus.Failed ? job.Error : null,
        Printers = status == HpDiscoveryJobStatus.Completed ? [.. job.Printers] : []
    };
}
