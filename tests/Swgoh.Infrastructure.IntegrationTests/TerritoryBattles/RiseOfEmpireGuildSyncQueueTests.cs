using Swgoh.Application.Abstractions;
using Swgoh.Application.TerritoryBattles;
using Swgoh.Infrastructure.TerritoryBattles;

namespace Swgoh.Infrastructure.IntegrationTests.TerritoryBattles;

public sealed class RiseOfEmpireGuildSyncQueueTests
{
    [Fact]
    public async Task StartAsync_ConcurrentRequestsForSamePlayer_ReusesActiveJob()
    {
        var repository = new FakeJobRepository();
        using var queue = new RiseOfEmpireGuildSyncQueue(repository, new FakeClock());

        Task<RiseOfEmpireGuildSyncJob>[] starts =
        [
            .. Enumerable.Range(0, 8).Select(_ => queue.StartAsync(476_825_771))
        ];

        RiseOfEmpireGuildSyncJob[] jobs = await Task.WhenAll(starts);

        Assert.Single(jobs.Select(job => job.Id).Distinct(StringComparer.Ordinal));
        Assert.Single(repository.Jobs);
        Assert.Equal(RiseOfEmpireGuildSyncStatus.Queued, jobs[0].Status);
    }

    [Fact]
    public async Task StartAsync_AfterTerminalJob_CreatesNewJob()
    {
        var repository = new FakeJobRepository();
        using var queue = new RiseOfEmpireGuildSyncQueue(repository, new FakeClock());
        RiseOfEmpireGuildSyncJob first = await queue.StartAsync(476_825_771);
        await repository.UpsertAsync(first with { Status = RiseOfEmpireGuildSyncStatus.Completed });

        RiseOfEmpireGuildSyncJob second = await queue.StartAsync(476_825_771);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, repository.Jobs.Count);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeJobRepository : IRiseOfEmpireGuildSyncJobRepository
    {
        private readonly object gate = new();
        private readonly Dictionary<string, RiseOfEmpireGuildSyncJob> jobs = new(StringComparer.Ordinal);

        public IReadOnlyCollection<RiseOfEmpireGuildSyncJob> Jobs
        {
            get
            {
                lock (gate)
                {
                    return [.. jobs.Values];
                }
            }
        }

        public Task<RiseOfEmpireGuildSyncJob?> FindByIdAsync(
            string jobId,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                jobs.TryGetValue(jobId, out RiseOfEmpireGuildSyncJob? job);
                return Task.FromResult(job);
            }
        }

        public Task<RiseOfEmpireGuildSyncJob?> FindLatestByAllyCodeAsync(
            long allyCode,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                RiseOfEmpireGuildSyncJob? latest = jobs.Values
                    .Where(job => job.AllyCode == allyCode)
                    .OrderByDescending(job => job.CreatedAtUtc)
                    .ThenByDescending(job => job.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                return Task.FromResult(latest);
            }
        }

        public Task<IReadOnlyCollection<RiseOfEmpireGuildSyncJob>> FindRecoverableAsync(
            int maxJobs = 16,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                IReadOnlyCollection<RiseOfEmpireGuildSyncJob> recoverable =
                [
                    .. jobs.Values
                        .Where(job => !job.IsTerminal)
                        .OrderBy(job => job.CreatedAtUtc)
                        .Take(maxJobs)
                ];
                return Task.FromResult(recoverable);
            }
        }

        public Task UpsertAsync(
            RiseOfEmpireGuildSyncJob job,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                jobs[job.Id] = job;
            }

            return Task.CompletedTask;
        }
    }
}
