namespace api.Services.Printing.Interfaces;

public interface IPrintJobProcessor
{
    /// <summary>Puts jobs left in <c>Processing</c> by a crash/restart back in the queue. Called once when the worker starts.</summary>
    Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>One queue pass: updates the state of jobs the printers are printing, then sends every due queued job.</summary>
    Task ProcessAsync(CancellationToken cancellationToken = default);
}
