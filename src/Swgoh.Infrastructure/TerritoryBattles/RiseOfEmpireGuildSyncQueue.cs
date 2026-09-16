using System.Threading.Channels;

using Swgoh.Application.Abstractions;
using Swgoh.Application.TerritoryBattles;

namespace Swgoh.Infrastructure.TerritoryBattles;

internal sealed class RiseOfEmpireGuildSyncQueue(
    IRiseOfEmpireGuildSyncJobRepository repository,
    IClock clock) : IRiseOfEmpireGuildSyncQueue, IDisposable
{
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    private readonly SemaphoreSlim startGate = new(1, 1);
    private int disposed;

    internal ChannelReader<string> Reader => queue.Reader;

    public async Task<RiseOfEmpireGuildSyncJob> StartAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(allyCode, 100_000_000L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(allyCode, 999_999_999L);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        await startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RiseOfEmpireGuildSyncJob? latest = await repository
                .FindLatestByAllyCodeAsync(allyCode, cancellationToken)
                .ConfigureAwait(false);
            if (latest is not null && !latest.IsTerminal)
            {
                return latest;
            }

            DateTimeOffset now = clock.UtcNow;
            var job = new RiseOfEmpireGuildSyncJob(
                $"rote-guild:{allyCode}:{Guid.NewGuid():N}",
                allyCode,
                RiseOfEmpireGuildSyncStatus.Queued,
                0,
                0,
                0,
                null,
                null,
                null,
                now,
                now);
            await repository.UpsertAsync(job, cancellationToken).ConfigureAwait(false);
            await queue.Writer.WriteAsync(job.Id, cancellationToken).ConfigureAwait(false);
            return job;
        }
        finally
        {
            startGate.Release();
        }
    }

    public Task<RiseOfEmpireGuildSyncJob?> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default) =>
        repository.FindByIdAsync(jobId, cancellationToken);

    public Task<RiseOfEmpireGuildSyncJob?> GetLatestAsync(
        long allyCode,
        CancellationToken cancellationToken = default) =>
        repository.FindLatestByAllyCodeAsync(allyCode, cancellationToken);

    internal async Task ResumePendingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<RiseOfEmpireGuildSyncJob> jobs = await repository
            .FindRecoverableAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        foreach (RiseOfEmpireGuildSyncJob job in jobs)
        {
            await queue.Writer.WriteAsync(job.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            queue.Writer.TryComplete();
            startGate.Dispose();
        }
    }
}
