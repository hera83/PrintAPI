using api.Services.Printing;
using api.Services.Printing.Interfaces;

namespace api.BgWorkers;

/// <summary>
/// Drives the print queue: every few seconds (or right away when <see cref="PrintQueueSignal"/> fires) it lets
/// <see cref="IPrintJobProcessor"/> send queued jobs and follow up on jobs the printers are printing.
/// </summary>
public class PrintQueueWorker(
    IServiceScopeFactory scopeFactory,
    PrintQueueSignal signal,
    ILogger<PrintQueueWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync(processor => processor.RecoverInterruptedJobsAsync(stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunAsync(processor => processor.ProcessAsync(stoppingToken), stoppingToken);

            try
            {
                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when the host is shutting down.
                return;
            }
        }
    }

    private async Task RunAsync(Func<IPrintJobProcessor, Task> action, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<IPrintJobProcessor>());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected when the host is shutting down mid-pass.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Print queue pass failed");
        }
    }
}
