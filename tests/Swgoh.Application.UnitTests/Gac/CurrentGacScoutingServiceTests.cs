using Swgoh.Application.Gac;
using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class CurrentGacScoutingServiceTests
{
    [Theory]
    [InlineData(GacFormat.ThreeVsThree)]
    [InlineData(GacFormat.FiveVsFive)]
    public async Task GetAsync_ScoutsBothRostersAndBuildsBattlePlan(GacFormat activeFormat)
    {
        const long playerAllyCode = 123456789;
        const long opponentAllyCode = 987654321;
        var source = new FakeOpponentSource(new CurrentGacOpponent(
            playerAllyCode,
            opponentAllyCode,
            "Opponent",
            "opponent-player-id",
            GacLeague.Kyber,
            activeFormat,
            "event",
            "event:instance",
            "event:instance:KYBER:1",
            2,
            "SeasonStatus",
            "DirectBracketMetadata"));
        var scouting = new RecordingScoutingService();
        var profiles = new RecordingPlayerProfileService(playerAllyCode, opponentAllyCode);
        var roster = new RecordingPlayerRosterService(playerAllyCode, opponentAllyCode);
        var historySync = new RecordingHistorySyncService();
        var counters = new EmptyCounterStatisticsService();
        var service = new CurrentGacScoutingService(
            source,
            scouting,
            profiles,
            roster,
            historySync,
            counters);

        CurrentGacScoutingResult result = await service.GetAsync(
            playerAllyCode,
            formatOverride: null,
            maxRounds: 30,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.Found, result.Lookup.Status);
        Assert.Equal(1, scouting.CallCount);
        Assert.Equal(opponentAllyCode, scouting.LastAllyCode);
        Assert.Equal(activeFormat, scouting.LastFormat);
        Assert.Equal(GacLeague.Kyber, scouting.LastTargetLeague);
        Assert.Equal(1, historySync.CallCount);
        Assert.Equal(opponentAllyCode, historySync.LastAllyCode);
        Assert.Equal(activeFormat, historySync.LastFormat);
        Assert.IsType<GacHistorySyncResult>(result.HistorySync);
        Assert.Equal(2, profiles.RefreshCallCount);
        Assert.Contains(playerAllyCode, profiles.RefreshedAllyCodes);
        Assert.Contains(opponentAllyCode, profiles.RefreshedAllyCodes);
        Assert.Equal(0, roster.PageCallCount);
        Assert.Equal(2, roster.SnapshotCallCount);

        CurrentOpponentRosterScouting rosterScouting = Assert.IsType<CurrentOpponentRosterScouting>(result.RosterScouting);
        Assert.Equal(opponentAllyCode, rosterScouting.Analysis.AllyCode);
        Assert.Equal(12_000_000, rosterScouting.Analysis.GalacticPower);
        Assert.Single(rosterScouting.GalacticLegends);
        Assert.Equal("GL_TEST", rosterScouting.GalacticLegends.Single().DefinitionId);
        Assert.Equal(2, rosterScouting.TopCharacters.Count);
        Assert.Single(rosterScouting.TopShips);
        Assert.Single(rosterScouting.OmicronCharacters);

        CurrentGacBattlePlan battlePlan = Assert.IsType<CurrentGacBattlePlan>(result.BattlePlan);
        Assert.Equal(11_000_000, battlePlan.Comparison.PlayerGalacticPower);
        Assert.Equal(12_000_000, battlePlan.Comparison.OpponentGalacticPower);
        Assert.Equal(-1_000_000, battlePlan.Comparison.GalacticPowerDelta);
        Assert.Equal(1, battlePlan.Comparison.PlayerGalacticLegends);
        Assert.Equal(1, battlePlan.Comparison.OpponentGalacticLegends);
        Assert.Contains(battlePlan.Threats, threat => threat.Unit.DefinitionId == "GL_TEST");
        Assert.Contains(battlePlan.AttackReserves, reserve => reserve.Unit.DefinitionId == "GL_SELF");
        GacBattleCounterSuggestion counter = Assert.Single(
            battlePlan.CounterSuggestions,
            item => item.Threat.DefinitionId == "GL_TEST");
        Assert.Equal("RosterStrengthHeuristic", counter.Source);
        Assert.Contains(counter.CandidateAnchors, candidate => candidate.DefinitionId == "GL_SELF");
        Assert.True(counter.RequiresDatacronVerification);
        Assert.Contains(battlePlan.Warnings, warning => warning.Contains("No historical GAC rounds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenOptionalScoutingDependenciesFail_ReturnsDegradedFoundResult()
    {
        const long playerAllyCode = 123456789;
        const long opponentAllyCode = 987654321;
        var source = new FakeOpponentSource(new CurrentGacOpponent(
            playerAllyCode,
            opponentAllyCode,
            "Opponent",
            "opponent-player-id",
            GacLeague.Kyber,
            GacFormat.ThreeVsThree,
            "event",
            "event:instance",
            "event:instance:KYBER:1",
            2,
            "SeasonStatus",
            "DirectBracketMetadata"));
        var profiles = new RecordingPlayerProfileService(playerAllyCode, opponentAllyCode);
        var roster = new RecordingPlayerRosterService(playerAllyCode, opponentAllyCode);
        var service = new CurrentGacScoutingService(
            source,
            new ThrowingScoutingService(),
            profiles,
            roster,
            new ThrowingHistorySyncService(),
            new ThrowingCounterStatisticsService());

        CurrentGacScoutingResult result = await service.GetAsync(
            playerAllyCode,
            formatOverride: null,
            maxRounds: 30,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.Found, result.Lookup.Status);
        Assert.NotNull(result.Lookup.Opponent);
        Assert.Null(result.HistorySync);
        Assert.Null(result.Scouting);
        Assert.NotNull(result.RosterScouting);
        Assert.NotNull(result.BattlePlan);
        Assert.Contains(result.DegradationWarnings, warning => warning.Contains("histórico de GAC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.DegradationWarnings, warning => warning.Contains("scouting histórico", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.DegradationWarnings, warning => warning.Contains("counters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAsync_WhenOpponentIsUnavailable_DoesNotRefreshOrRunScouting()
    {
        const long playerAllyCode = 123456789;
        const long opponentAllyCode = 987654321;
        var source = new FakeOpponentSource(CurrentGacOpponentLookup.Unavailable(
            CurrentGacOpponentStatus.NoActiveEvent,
            "No active event."));
        var scouting = new RecordingScoutingService();
        var profiles = new RecordingPlayerProfileService(playerAllyCode, opponentAllyCode);
        var roster = new RecordingPlayerRosterService(playerAllyCode, opponentAllyCode);
        var historySync = new RecordingHistorySyncService();
        var counters = new EmptyCounterStatisticsService();
        var service = new CurrentGacScoutingService(
            source,
            scouting,
            profiles,
            roster,
            historySync,
            counters);

        CurrentGacScoutingResult result = await service.GetAsync(
            playerAllyCode,
            GacFormat.ThreeVsThree,
            30,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.NoActiveEvent, result.Lookup.Status);
        Assert.Equal(0, scouting.CallCount);
        Assert.Equal(0, historySync.CallCount);
        Assert.Equal(0, profiles.RefreshCallCount);
        Assert.Equal(0, roster.PageCallCount);
        Assert.Equal(0, roster.SnapshotCallCount);
        Assert.Null(result.RosterScouting);
        Assert.Null(result.BattlePlan);
        Assert.Null(result.HistorySync);
    }

    private sealed class FakeOpponentSource : ICurrentGacOpponentSource
    {
        private readonly CurrentGacOpponentLookup lookup;

        public FakeOpponentSource(CurrentGacOpponent opponent)
            : this(CurrentGacOpponentLookup.Found(opponent))
        {
        }

        public FakeOpponentSource(CurrentGacOpponentLookup lookup)
        {
            this.lookup = lookup;
        }

        public Task<CurrentGacOpponentLookup> GetAsync(
            long allyCode,
            GacFormat? formatOverride,
            CancellationToken cancellationToken = default) => Task.FromResult(lookup);
    }

    private sealed class RecordingScoutingService : IOpponentScoutingService
    {
        public int CallCount { get; private set; }
        public long LastAllyCode { get; private set; }
        public GacFormat LastFormat { get; private set; }
        public GacLeague? LastTargetLeague { get; private set; }

        public Task<OpponentScoutingReport?> GetAsync(
            long allyCode,
            GacFormat format,
            GacLeague? targetLeague,
            int maxRounds,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastAllyCode = allyCode;
            LastFormat = format;
            LastTargetLeague = targetLeague;
            return Task.FromResult<OpponentScoutingReport?>(null);
        }
    }

    private sealed class ThrowingScoutingService : IOpponentScoutingService
    {
        public Task<OpponentScoutingReport?> GetAsync(
            long allyCode,
            GacFormat format,
            GacLeague? targetLeague,
            int maxRounds,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("history unavailable");
    }

    private sealed class RecordingHistorySyncService : IGacHistorySyncService
    {
        public int CallCount { get; private set; }
        public long LastAllyCode { get; private set; }
        public GacFormat LastFormat { get; private set; }

        public Task<GacHistorySyncResult> SyncAsync(
            long allyCode,
            GacFormat format,
            int maxRounds,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastAllyCode = allyCode;
            LastFormat = format;
            return Task.FromResult(new GacHistorySyncResult(0, 0, 0, 0, [], []));
        }
    }

    private sealed class ThrowingHistorySyncService : IGacHistorySyncService
    {
        public Task<GacHistorySyncResult> SyncAsync(
            long allyCode,
            GacFormat format,
            int maxRounds,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("sync unavailable");
    }

    private sealed class EmptyCounterStatisticsService : IGacCounterStatisticsService
    {
        public Task<IReadOnlyCollection<GacCounterStatistics>> GetAsync(
            GacCounterStatisticsQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacCounterStatistics>>([]);
    }

    private sealed class ThrowingCounterStatisticsService : IGacCounterStatisticsService
    {
        public Task<IReadOnlyCollection<GacCounterStatistics>> GetAsync(
            GacCounterStatisticsQuery query,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("counter source unavailable");
    }

    private sealed class RecordingPlayerProfileService(long playerAllyCode, long opponentAllyCode) : IPlayerProfileService
    {
        private readonly List<long> refreshedAllyCodes = [];

        public int RefreshCallCount => refreshedAllyCodes.Count;
        public IReadOnlyCollection<long> RefreshedAllyCodes => refreshedAllyCodes;

        public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<PlayerProfile?>(null);

        public Task<PlayerProfile> SaveAsync(
            long allyCode,
            string name,
            long galacticPower,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlayerProfile> RefreshFromGameAsync(
            long allyCode,
            CancellationToken cancellationToken = default)
        {
            refreshedAllyCodes.Add(allyCode);
            if (allyCode == playerAllyCode)
            {
                return Task.FromResult(PlayerProfile.Import(
                    playerAllyCode,
                    "player-id",
                    "Player",
                    null,
                    null,
                    85,
                    11_000_000,
                    DateTimeOffset.Parse("2026-09-13T14:00:00Z"),
                    [
                        new RosterUnit("gl-self", "GL_SELF", 85, 7, 13, 9, 6, 55_000, false, 6, 1),
                        new RosterUnit("omi-self", "OMI_SELF", 85, 7, 13, 8, 6, 45_000, false, 4, 1),
                        new RosterUnit("ship-self", "SHIP_SELF", 85, 7, 1, 0, 0, 75_000, true)
                    ]));
            }

            Assert.Equal(opponentAllyCode, allyCode);
            return Task.FromResult(PlayerProfile.Import(
                opponentAllyCode,
                "opponent-player-id",
                "Opponent",
                null,
                null,
                85,
                12_000_000,
                DateTimeOffset.Parse("2026-09-13T14:00:00Z"),
                [
                    new RosterUnit("gl", "GL_TEST", 85, 7, 13, 9, 6, 50_000, false, 6, 1),
                    new RosterUnit("ship", "SHIP_TEST", 85, 7, 1, 0, 0, 70_000, true)
                ]));
        }
    }

    private sealed class RecordingPlayerRosterService(long playerAllyCode, long opponentAllyCode) : IPlayerRosterService
    {
        public int PageCallCount { get; private set; }
        public int SnapshotCallCount { get; private set; }

        public Task<PlayerRosterPage?> GetAsync(
            long allyCode,
            PlayerRosterQuery query,
            CancellationToken cancellationToken = default)
        {
            PageCallCount++;
            Assert.True(allyCode == playerAllyCode || allyCode == opponentAllyCode);

            PlayerRosterUnit[] items = allyCode == playerAllyCode
                ? PlayerItems(query)
                : OpponentItems(query);

            return Task.FromResult<PlayerRosterPage?>(new PlayerRosterPage(
                allyCode,
                DateTimeOffset.Parse("2026-09-13T14:00:00Z"),
                items.Length,
                1,
                query.PageSize,
                1,
                items));
        }

        public Task<PlayerRosterSnapshot?> GetSnapshotAsync(
            long allyCode,
            CancellationToken cancellationToken = default)
        {
            SnapshotCallCount++;
            Assert.True(allyCode == playerAllyCode || allyCode == opponentAllyCode);
            PlayerRosterUnit[] units = allyCode == playerAllyCode
                ?
                [
                    Unit("gl-self", "GL_SELF", "Player Legend", 55_000, relic: 9, tags: ["galactic_legend"], omicrons: 1),
                    Unit("omi-self", "OMI_SELF", "Player Omicron", 45_000, relic: 8, omicrons: 1),
                    Unit("ship-self", "SHIP_SELF", "Player Capital Ship", 75_000, isShip: true)
                ]
                :
                [
                    Unit("gl", "GL_TEST", "Opponent Legend", 50_000, relic: 9, tags: ["galactic_legend"], omicrons: 1),
                    Unit("char", "CHAR_TEST", "Opponent Character", 40_000, relic: 8),
                    Unit("ship", "SHIP_TEST", "Opponent Capital Ship", 70_000, isShip: true)
                ];

            return Task.FromResult<PlayerRosterSnapshot?>(new PlayerRosterSnapshot(
                allyCode,
                DateTimeOffset.Parse("2026-09-13T14:00:00Z"),
                allyCode == playerAllyCode ? "Player" : "Opponent",
                allyCode == playerAllyCode ? 11_000_000 : 12_000_000,
                units.Length,
                units,
                []));
        }

        private static PlayerRosterUnit[] PlayerItems(PlayerRosterQuery query) => query.Type == PlayerRosterUnitType.Ship
            ? [Unit("ship-self", "SHIP_SELF", "Player Capital Ship", 75_000, isShip: true)]
            : query.HasOmicron is true
                ? [Unit("omi-self", "OMI_SELF", "Player Omicron", 45_000, relic: 8, omicrons: 1)]
                :
                [
                    Unit("gl-self", "GL_SELF", "Player Legend", 55_000, relic: 9, tags: ["galactic_legend"], omicrons: 1),
                    Unit("omi-self", "OMI_SELF", "Player Omicron", 45_000, relic: 8, omicrons: 1)
                ];

        private static PlayerRosterUnit[] OpponentItems(PlayerRosterQuery query) => query.Type == PlayerRosterUnitType.Ship
            ? [Unit("ship", "SHIP_TEST", "Opponent Capital Ship", 70_000, isShip: true)]
            : query.HasOmicron is true
                ? [Unit("gl", "GL_TEST", "Opponent Legend", 50_000, relic: 9, tags: ["galactic_legend"], omicrons: 1)]
                :
                [
                    Unit("gl", "GL_TEST", "Opponent Legend", 50_000, relic: 9, tags: ["galactic_legend"], omicrons: 1),
                    Unit("char", "CHAR_TEST", "Opponent Character", 40_000, relic: 8)
                ];

        private static PlayerRosterUnit Unit(
            string id,
            string definitionId,
            string name,
            long gp,
            bool isShip = false,
            int relic = 9,
            IReadOnlyCollection<string>? tags = null,
            int omicrons = 0) => new(
                id,
                definitionId,
                name,
                null,
                null,
                [],
                tags ?? [],
                85,
                7,
                isShip ? 1 : 13,
                isShip ? 0 : relic,
                isShip ? 0 : 6,
                gp,
                isShip,
                isShip ? 0 : 6,
                omicrons);
    }
}
