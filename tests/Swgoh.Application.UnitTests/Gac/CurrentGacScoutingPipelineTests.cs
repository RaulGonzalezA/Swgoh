using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class CurrentGacScoutingPipelineTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-09-14T20:00:00Z");

    [Fact]
    public async Task GetAsync_StartsIndependentPipelinesWithoutWaitingForSlowPlayerRefresh()
    {
        const long playerAllyCode = 123456789;
        const long opponentAllyCode = 987654321;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        var playerRefreshRelease = new TaskCompletionSource<PlayerProfile>(TaskCreationOptions.RunContinuationsAsynchronously);
        var historyRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opponentSnapshotStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var historicalScoutingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        PlayerProfile opponentProfile = CreateProfile(opponentAllyCode, "Opponent", 12_000_000);
        PlayerProfile playerProfile = CreateProfile(playerAllyCode, "Player", 11_000_000);

        var service = new CurrentGacScoutingService(
            new FixedOpponentSource(CreateOpponent(playerAllyCode, opponentAllyCode)),
            new CoordinatedScoutingService(historicalScoutingStarted),
            new CoordinatedProfileService(
                playerAllyCode,
                opponentAllyCode,
                opponentProfile,
                playerRefreshRelease),
            new CoordinatedRosterService(
                playerAllyCode,
                opponentAllyCode,
                playerProfile,
                opponentProfile,
                opponentSnapshotStarted),
            new CoordinatedHistorySyncService(historyRelease),
            new EmptyCounterStatisticsService(),
            new GacTelemetry());

        Task<CurrentGacScoutingResult> resultTask = service.GetAsync(
            playerAllyCode,
            formatOverride: null,
            maxRounds: 30,
            cancellationToken);

        await opponentSnapshotStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        Assert.False(playerRefreshRelease.Task.IsCompleted);
        Assert.False(historyRelease.Task.IsCompleted);
        Assert.False(historicalScoutingStarted.Task.IsCompleted);

        historyRelease.SetResult();
        await historicalScoutingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        playerRefreshRelease.SetResult(playerProfile);
        CurrentGacScoutingResult result = await resultTask;

        Assert.Equal(CurrentGacOpponentStatus.Found, result.Lookup.Status);
        Assert.NotNull(result.RosterScouting);
        Assert.NotNull(result.BattlePlan);
    }

    [Fact]
    public async Task GetAsync_WithProfilesUpdatedEightMinutesAgo_ReusesPersistedProfilesWithoutRefresh()
    {
        const long playerAllyCode = 123456789;
        const long opponentAllyCode = 987654321;
        PlayerProfile playerProfile = CreateProfile(
            playerAllyCode,
            "Player",
            11_000_000,
            FixedNow - TimeSpan.FromMinutes(8));
        PlayerProfile opponentProfile = CreateProfile(
            opponentAllyCode,
            "Opponent",
            12_000_000,
            FixedNow - TimeSpan.FromMinutes(8));
        var profileService = new RecordingProfileService(playerProfile, opponentProfile);
        var service = CreateFreshnessService(
            playerAllyCode,
            opponentAllyCode,
            playerProfile,
            opponentProfile,
            profileService);

        CurrentGacScoutingResult result = await service.GetAsync(
            playerAllyCode,
            formatOverride: null,
            maxRounds: 30,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.Found, result.Lookup.Status);
        Assert.Equal(2, profileService.GetCallCount);
        Assert.Equal(0, profileService.RefreshCallCount);
    }

    [Fact]
    public async Task GetAsync_WithProfilesOlderThanTenMinutes_RefreshesProfiles()
    {
        const long playerAllyCode = 123456789;
        const long opponentAllyCode = 987654321;
        PlayerProfile playerProfile = CreateProfile(
            playerAllyCode,
            "Player",
            11_000_000,
            FixedNow - TimeSpan.FromMinutes(11));
        PlayerProfile opponentProfile = CreateProfile(
            opponentAllyCode,
            "Opponent",
            12_000_000,
            FixedNow - TimeSpan.FromMinutes(11));
        var profileService = new RecordingProfileService(playerProfile, opponentProfile);
        var service = CreateFreshnessService(
            playerAllyCode,
            opponentAllyCode,
            playerProfile,
            opponentProfile,
            profileService);

        CurrentGacScoutingResult result = await service.GetAsync(
            playerAllyCode,
            formatOverride: null,
            maxRounds: 30,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.Found, result.Lookup.Status);
        Assert.Equal(2, profileService.GetCallCount);
        Assert.Equal(2, profileService.RefreshCallCount);
    }

    private static CurrentGacScoutingService CreateFreshnessService(
        long playerAllyCode,
        long opponentAllyCode,
        PlayerProfile playerProfile,
        PlayerProfile opponentProfile,
        RecordingProfileService profileService) => new(
        new FixedOpponentSource(CreateOpponent(playerAllyCode, opponentAllyCode)),
        new EmptyScoutingService(),
        profileService,
        new StaticRosterService(playerProfile, opponentProfile),
        new ImmediateHistorySyncService(),
        new EmptyCounterStatisticsService(),
        new GacTelemetry(),
        new FixedClock(FixedNow));

    private static CurrentGacOpponent CreateOpponent(long playerAllyCode, long opponentAllyCode) => new(
        playerAllyCode,
        opponentAllyCode,
        "Opponent",
        "opponent-player-id",
        GacLeague.Kyber,
        GacFormat.ThreeVsThree,
        "event",
        "event:instance",
        "event:instance:KYBER:4178",
        1,
        "SeasonStatus",
        "RatingBinarySearch");

    private static PlayerProfile CreateProfile(
        long allyCode,
        string name,
        long galacticPower,
        DateTimeOffset? updatedAtUtc = null) => PlayerProfile.Import(
        allyCode,
        $"player-{allyCode}",
        name,
        null,
        null,
        85,
        galacticPower,
        updatedAtUtc ?? DateTimeOffset.Parse("2026-09-14T18:00:00Z"),
        []);

    private sealed class FixedOpponentSource(CurrentGacOpponent opponent) : ICurrentGacOpponentSource
    {
        public Task<CurrentGacOpponentLookup> GetAsync(
            long allyCode,
            GacFormat? formatOverride,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentGacOpponentLookup.Found(opponent));
    }

    private sealed class CoordinatedProfileService(
        long playerAllyCode,
        long opponentAllyCode,
        PlayerProfile opponentProfile,
        TaskCompletionSource<PlayerProfile> playerRefreshRelease) : IPlayerProfileService
    {
        public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<PlayerProfile?>(allyCode == opponentAllyCode ? opponentProfile : null);

        public Task<PlayerProfile> SaveAsync(
            long allyCode,
            string name,
            long galacticPower,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlayerProfile> RefreshFromGameAsync(
            long allyCode,
            CancellationToken cancellationToken = default)
        {
            if (allyCode == opponentAllyCode)
            {
                return Task.FromResult(opponentProfile);
            }

            Assert.Equal(playerAllyCode, allyCode);
            return playerRefreshRelease.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class RecordingProfileService(params PlayerProfile[] profiles) : IPlayerProfileService
    {
        private readonly IReadOnlyDictionary<long, PlayerProfile> profiles = profiles.ToDictionary(profile => profile.AllyCode);

        public int GetCallCount { get; private set; }
        public int RefreshCallCount { get; private set; }

        public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(profiles.TryGetValue(allyCode, out PlayerProfile? profile) ? profile : null);
        }

        public Task<PlayerProfile> SaveAsync(
            long allyCode,
            string name,
            long galacticPower,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlayerProfile> RefreshFromGameAsync(
            long allyCode,
            CancellationToken cancellationToken = default)
        {
            RefreshCallCount++;
            return Task.FromResult(profiles[allyCode]);
        }
    }

    private sealed class CoordinatedRosterService(
        long playerAllyCode,
        long opponentAllyCode,
        PlayerProfile playerProfile,
        PlayerProfile opponentProfile,
        TaskCompletionSource opponentSnapshotStarted) : IPlayerRosterService
    {
        public Task<PlayerRosterPage?> GetAsync(
            long allyCode,
            PlayerRosterQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlayerRosterSnapshot?> GetSnapshotAsync(
            long allyCode,
            CancellationToken cancellationToken = default)
        {
            PlayerProfile profile;
            if (allyCode == opponentAllyCode)
            {
                opponentSnapshotStarted.TrySetResult();
                profile = opponentProfile;
            }
            else
            {
                Assert.Equal(playerAllyCode, allyCode);
                profile = playerProfile;
            }

            return Task.FromResult<PlayerRosterSnapshot?>(CreateSnapshot(profile));
        }
    }

    private sealed class StaticRosterService(params PlayerProfile[] profiles) : IPlayerRosterService
    {
        private readonly IReadOnlyDictionary<long, PlayerProfile> profiles = profiles.ToDictionary(profile => profile.AllyCode);

        public Task<PlayerRosterPage?> GetAsync(
            long allyCode,
            PlayerRosterQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlayerRosterSnapshot?> GetSnapshotAsync(
            long allyCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PlayerRosterSnapshot?>(CreateSnapshot(profiles[allyCode]));
    }

    private static PlayerRosterSnapshot CreateSnapshot(PlayerProfile profile) => new(
        profile.AllyCode,
        profile.UpdatedAtUtc,
        profile.Name,
        profile.GalacticPower,
        profile.Roster.Count,
        [],
        []);

    private sealed class CoordinatedHistorySyncService(TaskCompletionSource release) : IGacHistorySyncService
    {
        public async Task<GacHistorySyncResult> SyncAsync(
            long allyCode,
            GacFormat format,
            int maxRounds,
            CancellationToken cancellationToken = default)
        {
            await release.Task.WaitAsync(cancellationToken);
            return new GacHistorySyncResult(0, 0, 0, 0, [], []);
        }
    }

    private sealed class ImmediateHistorySyncService : IGacHistorySyncService
    {
        public Task<GacHistorySyncResult> SyncAsync(
            long allyCode,
            GacFormat format,
            int maxRounds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GacHistorySyncResult(0, 0, 0, 0, [], []));
    }

    private sealed class CoordinatedScoutingService(TaskCompletionSource started) : IOpponentScoutingService
    {
        public Task<OpponentScoutingReport?> GetAsync(
            long allyCode,
            GacFormat format,
            GacLeague? targetLeague,
            int maxRounds,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            return Task.FromResult<OpponentScoutingReport?>(null);
        }
    }

    private sealed class EmptyScoutingService : IOpponentScoutingService
    {
        public Task<OpponentScoutingReport?> GetAsync(
            long allyCode,
            GacFormat format,
            GacLeague? targetLeague,
            int maxRounds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OpponentScoutingReport?>(null);
    }

    private sealed class EmptyCounterStatisticsService : IGacCounterStatisticsService
    {
        public Task<IReadOnlyCollection<GacCounterStatistics>> GetAsync(
            GacCounterStatisticsQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacCounterStatistics>>([]);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
