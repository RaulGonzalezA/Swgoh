using Swgoh.Application.Abstractions;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Players;

public sealed class PlayerProfileServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshFromGameAsync_MapsPersistsAndSnapshotsImportedPlayer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var repository = new FakePlayerRepository();
        var snapshots = new FakePlayerSnapshotRepository();
        var client = new FakePlayerClient(CreateImportedPlayer());
        var service = CreateService(repository, snapshots, client);

        PlayerProfile result = await service.RefreshFromGameAsync(476_825_771, cancellationToken);

        Assert.Equal("player-id", result.PlayerId);
        Assert.Equal("Aberronko", result.Name);
        Assert.Equal("guild-id", result.GuildId);
        Assert.Equal(85, result.Level);
        Assert.Equal(70_000, result.GalacticPower);
        Assert.Equal(FixedNow, result.UpdatedAtUtc);
        Assert.Equal(2, result.Roster.Count);
        Assert.Same(result, repository.SavedPlayer);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, snapshots.UpsertCallCount);

        PlayerSnapshot snapshot = Assert.IsType<PlayerSnapshot>(snapshots.SavedSnapshot);
        Assert.Equal(70_000, snapshot.GalacticPower);
        Assert.Equal(40_000, snapshot.CharacterGalacticPower);
        Assert.Equal(30_000, snapshot.ShipGalacticPower);
        Assert.Equal(1, snapshot.CharacterCount);
        Assert.Equal(1, snapshot.ShipCount);
        Assert.Equal(2, snapshot.Zetas);
        Assert.Equal(1, snapshot.Omicrons);
        Assert.Equal(FixedNow, snapshot.CapturedAtUtc);
    }

    [Fact]
    public async Task RefreshFromGameAsync_WhenImportedProfileAndSnapshotAreFresh_ReturnsStoredProfileWithoutProviderCall()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerProfile stored = CreateStoredProfile(FixedNow - TimeSpan.FromMinutes(4));
        var repository = new FakePlayerRepository { Player = stored };
        var snapshots = new FakePlayerSnapshotRepository();
        snapshots.Seed(PlayerRosterMetrics.CreateSnapshot(stored));
        var client = new FakePlayerClient();
        var service = CreateService(repository, snapshots, client);

        PlayerProfile result = await service.RefreshFromGameAsync(476_825_771, cancellationToken);

        Assert.Same(stored, result);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(1, snapshots.ExistsCallCount);
        Assert.Equal(0, snapshots.UpsertCallCount);
        Assert.Null(repository.SavedPlayer);
    }

    [Fact]
    public async Task RefreshFromGameAsync_WhenFreshImportedProfileHasNoSnapshot_RepairsSnapshotWithoutProviderCall()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerProfile stored = CreateStoredProfile(FixedNow - TimeSpan.FromMinutes(4));
        var repository = new FakePlayerRepository { Player = stored };
        var snapshots = new FakePlayerSnapshotRepository();
        var client = new FakePlayerClient();
        var service = CreateService(repository, snapshots, client);

        PlayerProfile result = await service.RefreshFromGameAsync(476_825_771, cancellationToken);

        Assert.Same(stored, result);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(1, snapshots.ExistsCallCount);
        Assert.Equal(1, snapshots.UpsertCallCount);
        Assert.Equal(PlayerRosterMetrics.CreateSnapshot(stored).Id, snapshots.SavedSnapshot?.Id);
        Assert.Null(repository.SavedPlayer);
    }

    [Fact]
    public async Task RefreshFromGameAsync_WhenSnapshotWriteFails_NextRefreshRepairsWithoutCallingProviderAgain()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var repository = new FakePlayerRepository();
        var snapshots = new FakePlayerSnapshotRepository { FailuresRemaining = 1 };
        var client = new FakePlayerClient(CreateImportedPlayer());
        var service = CreateService(repository, snapshots, client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshFromGameAsync(476_825_771, cancellationToken));

        PlayerProfile persisted = Assert.IsType<PlayerProfile>(repository.Player);
        Assert.Equal(FixedNow, persisted.UpdatedAtUtc);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, snapshots.UpsertCallCount);
        Assert.Null(snapshots.SavedSnapshot);

        PlayerProfile repaired = await service.RefreshFromGameAsync(476_825_771, cancellationToken);

        Assert.Same(persisted, repaired);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, snapshots.ExistsCallCount);
        Assert.Equal(2, snapshots.UpsertCallCount);
        Assert.Equal(PlayerRosterMetrics.CreateSnapshot(persisted).Id, snapshots.SavedSnapshot?.Id);
    }

    [Fact]
    public async Task RefreshFromGameAsync_WhenRecentProfileWasCreatedManually_StillRefreshesFromGame()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerProfile stored = PlayerProfile.Create(
            476_825_771,
            "Manual player",
            1,
            FixedNow - TimeSpan.FromMinutes(1));
        var repository = new FakePlayerRepository { Player = stored };
        var snapshots = new FakePlayerSnapshotRepository();
        var client = new FakePlayerClient(CreateImportedPlayer());
        var service = CreateService(repository, snapshots, client);

        PlayerProfile result = await service.RefreshFromGameAsync(476_825_771, cancellationToken);

        Assert.Equal("player-id", result.PlayerId);
        Assert.Equal("Aberronko", result.Name);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, snapshots.UpsertCallCount);
    }

    [Fact]
    public async Task RefreshFromGameAsync_ConcurrentCallsForSamePlayer_CoalesceProviderRefresh()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var thirdFindReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new FakePlayerRepository(findCountToSignal: 3, findCountReached: thirdFindReached);
        var snapshots = new FakePlayerSnapshotRepository();
        var providerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakePlayerClient(async (_, token) =>
        {
            providerStarted.TrySetResult(true);
            await releaseProvider.Task.WaitAsync(token);
            return CreateImportedPlayer();
        });
        var refreshLock = new PlayerRefreshLock();
        PlayerProfileService firstService = CreateService(repository, snapshots, client, refreshLock);
        PlayerProfileService secondService = CreateService(repository, snapshots, client, refreshLock);

        Task<PlayerProfile> firstRefresh = firstService.RefreshFromGameAsync(476_825_771, cancellationToken);
        await providerStarted.Task.WaitAsync(cancellationToken);

        Task<PlayerProfile> secondRefresh = secondService.RefreshFromGameAsync(476_825_771, cancellationToken);
        await thirdFindReached.Task.WaitAsync(cancellationToken);

        Assert.Equal(1, client.CallCount);
        releaseProvider.TrySetResult(true);

        PlayerProfile[] results = await Task.WhenAll(firstRefresh, secondRefresh);

        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, snapshots.UpsertCallCount);
        Assert.Equal(1, snapshots.ExistsCallCount);
        Assert.Same(results[0], results[1]);
    }

    [Fact]
    public async Task SaveAsync_WhenPlayerDoesNotExist_CreatesAndPersistsPlayer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var repository = new FakePlayerRepository();
        var service = CreateService(
            repository,
            new FakePlayerSnapshotRepository(),
            new FakePlayerClient());

        PlayerProfile result = await service.SaveAsync(
            476_825_771,
            "  Aberronko  ",
            13_700_000,
            cancellationToken);

        Assert.Equal("Aberronko", result.Name);
        Assert.Equal(13_700_000, result.GalacticPower);
        Assert.Equal(FixedNow, result.UpdatedAtUtc);
        Assert.Empty(result.Roster);
        Assert.Same(result, repository.SavedPlayer);
    }

    private static PlayerProfileService CreateService(
        FakePlayerRepository repository,
        FakePlayerSnapshotRepository snapshots,
        FakePlayerClient client,
        PlayerRefreshLock? refreshLock = null) =>
        new(repository, snapshots, client, new FakeClock(FixedNow), refreshLock ?? new PlayerRefreshLock());

    private static PlayerProfile CreateStoredProfile(DateTimeOffset updatedAtUtc) =>
        PlayerProfile.Import(
            476_825_771,
            "player-id",
            "Aberronko",
            "guild-id",
            "Guild",
            85,
            70_000,
            updatedAtUtc,
            [
                new RosterUnit("char-1", "CHARACTER", 85, 7, 13, 9, 6, 40_000, false, 2, 1),
                new RosterUnit("ship-1", "SHIP", 85, 7, 1, 0, 0, 30_000, true)
            ]);

    private static ImportedPlayer CreateImportedPlayer() =>
        new(
            476_825_771,
            "player-id",
            "Aberronko",
            "guild-id",
            "Guild",
            85,
            70_000,
            [
                new ImportedRosterUnit("char-1", "CHARACTER", 85, 7, 13, 9, 6, 40_000, false, 2, 1),
                new ImportedRosterUnit("ship-1", "SHIP", 85, 7, 1, 0, 0, 30_000, true)
            ]);

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakePlayerRepository(
        int? findCountToSignal = null,
        TaskCompletionSource<bool>? findCountReached = null) : IPlayerRepository
    {
        private readonly object sync = new();
        private PlayerProfile? player;
        private int findCount;

        public PlayerProfile? Player
        {
            get
            {
                lock (sync)
                {
                    return player;
                }
            }
            set
            {
                lock (sync)
                {
                    player = value;
                }
            }
        }

        public PlayerProfile? SavedPlayer { get; private set; }

        public Task<PlayerProfile?> FindByAllyCodeAsync(long allyCode, CancellationToken cancellationToken = default)
        {
            int currentFindCount = Interlocked.Increment(ref findCount);
            if (findCountToSignal == currentFindCount)
            {
                findCountReached?.TrySetResult(true);
            }

            return Task.FromResult(Player);
        }

        public Task UpsertAsync(PlayerProfile value, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                SavedPlayer = value;
                player = value;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakePlayerSnapshotRepository : IPlayerSnapshotRepository
    {
        private readonly object sync = new();
        private readonly HashSet<string> ids = new(StringComparer.Ordinal);
        private int existsCallCount;
        private int upsertCallCount;

        public PlayerSnapshot? SavedSnapshot { get; private set; }
        public int ExistsCallCount => Volatile.Read(ref existsCallCount);
        public int UpsertCallCount => Volatile.Read(ref upsertCallCount);
        public int FailuresRemaining { get; set; }

        public void Seed(PlayerSnapshot snapshot)
        {
            lock (sync)
            {
                ids.Add(snapshot.Id);
            }
        }

        public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref existsCallCount);
            lock (sync)
            {
                return Task.FromResult(ids.Contains(id));
            }
        }

        public Task UpsertAsync(PlayerSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref upsertCallCount);
            lock (sync)
            {
                if (FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    return Task.FromException(new InvalidOperationException("Simulated snapshot persistence failure."));
                }

                SavedSnapshot = snapshot;
                ids.Add(snapshot.Id);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<PlayerSnapshot>> GetRecentAsync(
            long allyCode,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<PlayerSnapshot>>([]);
    }

    private sealed class FakePlayerClient : ISwgohPlayerClient
    {
        private readonly ImportedPlayer? player;
        private readonly Func<long, CancellationToken, Task<ImportedPlayer>>? handler;
        private int callCount;

        public FakePlayerClient(ImportedPlayer? player = null)
        {
            this.player = player;
        }

        public FakePlayerClient(Func<long, CancellationToken, Task<ImportedPlayer>> handler)
        {
            this.handler = handler;
        }

        public int CallCount => Volatile.Read(ref callCount);

        public async Task<ImportedPlayer> GetPlayerAsync(long allyCode, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);

            if (handler is not null)
            {
                return await handler(allyCode, cancellationToken);
            }

            return player ?? throw new InvalidOperationException("No imported player configured for this test.");
        }
    }
}
