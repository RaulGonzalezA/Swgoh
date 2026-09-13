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
    public async Task GetAsync_ScoutsOnlyDetectedActiveFormatAndRefreshesOpponentRoster(GacFormat activeFormat)
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
        var profiles = new RecordingPlayerProfileService(opponentAllyCode);
        var roster = new RecordingPlayerRosterService(opponentAllyCode);
        var service = new CurrentGacScoutingService(source, scouting, profiles, roster);

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
        Assert.Equal(1, profiles.RefreshCallCount);
        Assert.Equal(opponentAllyCode, profiles.LastRefreshAllyCode);
        Assert.Equal(3, roster.CallCount);

        CurrentOpponentRosterScouting rosterScouting = Assert.IsType<CurrentOpponentRosterScouting>(result.RosterScouting);
        Assert.Equal(opponentAllyCode, rosterScouting.Analysis.AllyCode);
        Assert.Equal(12_000_000, rosterScouting.Analysis.GalacticPower);
        Assert.Single(rosterScouting.GalacticLegends);
        Assert.Equal("GL_TEST", rosterScouting.GalacticLegends.Single().DefinitionId);
        Assert.Equal(2, rosterScouting.TopCharacters.Count);
        Assert.Single(rosterScouting.TopShips);
        Assert.Single(rosterScouting.OmicronCharacters);
    }

    [Fact]
    public async Task GetAsync_WhenOpponentIsUnavailable_DoesNotRefreshOrRunScouting()
    {
        var source = new FakeOpponentSource(CurrentGacOpponentLookup.Unavailable(
            CurrentGacOpponentStatus.NoActiveEvent,
            "No active event."));
        var scouting = new RecordingScoutingService();
        var profiles = new RecordingPlayerProfileService(987654321);
        var roster = new RecordingPlayerRosterService(987654321);
        var service = new CurrentGacScoutingService(source, scouting, profiles, roster);

        CurrentGacScoutingResult result = await service.GetAsync(
            123456789,
            GacFormat.ThreeVsThree,
            30,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.NoActiveEvent, result.Lookup.Status);
        Assert.Equal(0, scouting.CallCount);
        Assert.Equal(0, profiles.RefreshCallCount);
        Assert.Equal(0, roster.CallCount);
        Assert.Null(result.RosterScouting);
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

    private sealed class RecordingPlayerProfileService(long opponentAllyCode) : IPlayerProfileService
    {
        public int RefreshCallCount { get; private set; }
        public long LastRefreshAllyCode { get; private set; }

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
            RefreshCallCount++;
            LastRefreshAllyCode = allyCode;
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

    private sealed class RecordingPlayerRosterService(long opponentAllyCode) : IPlayerRosterService
    {
        public int CallCount { get; private set; }

        public Task<PlayerRosterPage?> GetAsync(
            long allyCode,
            PlayerRosterQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Assert.Equal(opponentAllyCode, allyCode);

            PlayerRosterUnit[] items = query.Type == PlayerRosterUnitType.Ship
                ? [Unit("ship", "SHIP_TEST", "Capital Ship", 70_000, isShip: true)]
                : query.HasOmicron is true
                    ? [Unit("gl", "GL_TEST", "Legend", 50_000, tags: ["galactic_legend"], omicrons: 1)]
                    :
                    [
                        Unit("gl", "GL_TEST", "Legend", 50_000, tags: ["galactic_legend"], omicrons: 1),
                        Unit("char", "CHAR_TEST", "Character", 40_000)
                    ];

            return Task.FromResult<PlayerRosterPage?>(new PlayerRosterPage(
                allyCode,
                DateTimeOffset.Parse("2026-09-13T14:00:00Z"),
                items.Length,
                1,
                query.PageSize,
                1,
                items));
        }

        private static PlayerRosterUnit Unit(
            string id,
            string definitionId,
            string name,
            long gp,
            bool isShip = false,
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
                isShip ? 0 : 9,
                isShip ? 0 : 6,
                gp,
                isShip,
                isShip ? 0 : 6,
                omicrons);
    }
}
