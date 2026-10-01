using System.Collections.Concurrent;
using System.Threading.Channels;
using api.Dtos.Hp;

namespace api.Services.Hp;

/// <summary>
/// In-memory queue and results of background printer discoveries (<c>POST /Hp/StartDiscover</c>). Only one discovery
/// runs at a time, since a subnet scan already opens hundreds of connections. Finished results are kept for an hour.
/// Nothing is persisted: a restart forgets them, which is fine since a discovery is cheap to start again.
/// </summary>
public sealed class HpDiscoveryJobStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, HpDiscoveryJob> _jobs = new();
    private readonly Channel<HpDiscoveryJob> _queue = Channel.CreateUnbounded<HpDiscoveryJob>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock _lock = new();

    /// <summary>Queues a new discovery, unless one is already queued or running — then returns false with that one.</summary>
    public bool TryEnqueue(HpDiscoveryRequestDto request, out HpDiscoveryJob job)
    {
        lock (_lock)
        {
            RemoveExpired();

            var active = _jobs.Values.FirstOrDefault(j => !j.IsFinished);
            if (active is not null)
            {
                job = active;
                return false;
            }

            job = new HpDiscoveryJob(request);
            _jobs[job.Id] = job;
            _queue.Writer.TryWrite(job);
            return true;
        }
    }

    public HpDiscoveryJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

    public IAsyncEnumerable<HpDiscoveryJob> DequeueAllAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAllAsync(cancellationToken);

    private void RemoveExpired()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var job in _jobs.Values)
        {
            if (job.IsFinished && job.CompletedAt < cutoff)
            {
                _jobs.TryRemove(job.Id, out _);
            }
        }
    }
}
