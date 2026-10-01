namespace api.Services.Printing;

/// <summary>Lets <c>PrintController</c> wake the queue worker immediately when a job is submitted, instead of waiting for its next poll.</summary>
public sealed class PrintQueueSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Notify()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signaled; the worker will pick up every due job on its next pass anyway.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _semaphore.WaitAsync(timeout, cancellationToken);
}
