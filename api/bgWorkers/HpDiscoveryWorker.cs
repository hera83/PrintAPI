using api.Services.Hp;
using api.Services.Hp.Interfaces;

namespace api.BgWorkers;

/// <summary>
/// Runs printer discoveries queued by <c>POST /Hp/StartDiscover</c>, one at a time, outside the HTTP request — a
/// subnet scan of a /16 takes minutes, longer than browsers, proxies and HTTP clients will keep a request open.
/// </summary>
public class HpDiscoveryWorker(
    IServiceScopeFactory scopeFactory,
    HpDiscoveryJobStore jobStore,
    ILogger<HpDiscoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in jobStore.DequeueAllAsync(stoppingToken))
            {
                await RunAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected when the host is shutting down.
        }
    }

    private async Task RunAsync(HpDiscoveryJob job, CancellationToken stoppingToken)
    {
        job.Start();
        logger.LogInformation("Printer discovery {JobId} started (scanSubnet={ScanSubnet})", job.Id, job.Request.ScanSubnet);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var discoveryService = scope.ServiceProvider.GetRequiredService<IHpPrinterDiscoveryService>();
            var printers = await discoveryService.DiscoverAsync(job.Request, job.Progress, stoppingToken);

            job.Complete(printers);
            logger.LogInformation("Printer discovery {JobId} completed with {Count} result(s)", job.Id, printers.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            job.Fail("The server shut down before the discovery finished.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Printer discovery {JobId} failed", job.Id);
            job.Fail(ex.Message);
        }
    }
}
