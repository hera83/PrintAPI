namespace api.Data.Models;

public enum PrintJobStatus
{
    /// <summary>Waiting in the queue (also while waiting for a retry after a transient failure).</summary>
    Queued,

    /// <summary>The background worker is converting and sending the document right now.</summary>
    Processing,

    /// <summary>The printer accepted the job and is printing it; its state is polled until it finishes.</summary>
    Printing,

    /// <summary>The printer reported the job as completed.</summary>
    Completed,

    /// <summary>The job could not be printed — see the status message.</summary>
    Failed,

    /// <summary>Canceled via the API (or on the printer).</summary>
    Canceled
}
