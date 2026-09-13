using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacCounterStatisticsServiceTests
{
    [Fact]
    public async Task GetAsync_AggregatesExactCounterTeamsAcrossPlayers()
    {
        GacHistoricalSquad defender = GacHistoricalSquad.Create("DEF", ["DEF2", "DEF3"], isFleet: false);
        GacHistoricalSquad attacker = GacHistoricalSquad.Create("ATK", ["ATK2", "ATK3"], isFleet: false);
        GacHistoricalRound[] rounds =
        [
            Round(111111111, 1, defender, attacker, won: true, banners: 57, attempt: 1),
            Round(222222222, 2, defender, attacker, won: false, banners: 0, attempt: 2)
        ];
        var service = new GacCounterStatisticsService(new FakeRepository(rounds));

        IReadOnlyCollection<GacCounterStatistics> result = await service.GetAsync(
            new GacCounterStatisticsQuery(
                GacFormat.ThreeVsThree,
                DefenderLeaderDefinitionId: "DEF",
                IsFleet: false),
            TestContext.Current.CancellationToken);

        GacCounterStatistics counter = Assert.Single(result);
        Assert.Equal("DEF", counter.DefenderLeaderDefinitionId);
        Assert.Equal(["DEF2", "DEF3"], counter.DefenderMemberDefinitionIds);
        Assert.Equal("ATK", counter.AttackerLeaderDefinitionId);
        Assert.Equal(["ATK2", "ATK3"], counter.AttackerMemberDefinitionIds);
        Assert.Equal(2, counter.Uses);
        Assert.Equal(1, counter.Wins);
        Assert.Equal(50m, counter.WinRate);
        Assert.Equal(1, counter.OneShots);
        Assert.Equal(50m, counter.OneShotRate);
        Assert.Equal(28.5m, counter.AverageBanners);
        Assert.Equal(1.5m, counter.AverageAttempt);
        Assert.Equal(2, counter.PlayersObserved);
    }

    private static GacHistoricalRound Round(
        long allyCode,
        int roundNumber,
        GacHistoricalSquad defender,
        GacHistoricalSquad attacker,
        bool won,
        int banners,
        int attempt) => GacHistoricalRound.Create(
        allyCode,
        season: 83,
        eventNumber: 1,
        roundNumber,
        GacFormat.ThreeVsThree,
        GacLeague.Kyber,
        DateTimeOffset.Parse($"2026-09-{9 + roundNumber:00}T18:00:00Z"),
        fullClear: null,
        source: "test",
        defenses: [],
        offenseBattles:
        [
            GacOffenseBattle.Create(
                "front",
                defender,
                attacker,
                won,
                banners,
                attempt,
                DateTimeOffset.Parse($"2026-09-{9 + roundNumber:00}T18:10:00Z"))
        ]);

    private sealed class FakeRepository(IReadOnlyCollection<GacHistoricalRound> rounds) : IGacHistoryRepository
    {
        public Task UpsertManyAsync(
            IReadOnlyCollection<GacHistoricalRound> roundsToSave,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyCollection<GacHistoricalRound>> GetAsync(
            long allyCode,
            GacFormat? format,
            int maxRounds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacHistoricalRound>>([]);

        public Task<IReadOnlyCollection<GacHistoricalRound>> GetRecentAsync(
            GacFormat format,
            int maxRounds,
            CancellationToken cancellationToken = default) => Task.FromResult(rounds);
    }
}
