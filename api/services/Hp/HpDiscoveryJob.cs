using api.Dtos.Hp;

namespace api.Services.Hp;

/// <summary>
/// One background printer discovery, kept in memory by <see cref="HpDiscoveryJobStore"/>. Only
/// <c>HpDiscoveryWorker</c> changes it; requests read it while it runs.
/// </summary>
public sealed class HpDiscoveryJob(HpDiscoveryRequestDto request)
{
    // Written last on every transition, so a reader that reads Status first sees the results/error that go with it.
    private volatile HpDiscoveryJobStatus _status = HpDiscoveryJobStatus.Queued;

    public Guid Id { get; } = Guid.NewGuid();
    public HpDiscoveryRequestDto Request { get; } = request;
    public HpDiscoveryProgress Progress { get; } = new();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public IReadOnlyList<DiscoveredHpPrinterDto> Printers { get; private set; } = [];
    public string? Error { get; private set; }

    public HpDiscoveryJobStatus Status => _status;
    public bool IsFinished => _status is HpDiscoveryJobStatus.Completed or HpDiscoveryJobStatus.Failed;

    public void Start()
    {
        StartedAt = DateTime.UtcNow;
        _status = HpDiscoveryJobStatus.Running;
    }

    public void Complete(IReadOnlyList<DiscoveredHpPrinterDto> printers)
    {
        Printers = printers;
        CompletedAt = DateTime.UtcNow;
        _status = HpDiscoveryJobStatus.Completed;
    }

    public void Fail(string error)
    {
        Error = error;
        CompletedAt = DateTime.UtcNow;
        _status = HpDiscoveryJobStatus.Failed;
    }
}
