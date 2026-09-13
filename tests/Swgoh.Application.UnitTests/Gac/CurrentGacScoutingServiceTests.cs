using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class CurrentGacScoutingServiceTests
{
    [Theory]
    [InlineData(GacFormat.ThreeVsThree)]
    [InlineData(GacFormat.FiveVsFive)]
    public async Task GetAsync_ScoutsOnlyDetectedActiveFormat(GacFormat activeFormat)
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
        var service = new CurrentGacScoutingService(source, scouting);

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
    }

    [Fact]
    public async Task GetAsync_WhenOpponentIsUnavailable_DoesNotRunScouting()
    {
        var source = new FakeOpponentSource(CurrentGacOpponentLookup.Unavailable(
            CurrentGacOpponentStatus.NoActiveEvent,
            "No active event."));
        var scouting = new RecordingScoutingService();
        var service = new CurrentGacScoutingService(source, scouting);

        CurrentGacScoutingResult result = await service.GetAsync(
            123456789,
            GacFormat.ThreeVsThree,
            30,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.NoActiveEvent, result.Lookup.Status);
        Assert.Equal(0, scouting.CallCount);
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
}
