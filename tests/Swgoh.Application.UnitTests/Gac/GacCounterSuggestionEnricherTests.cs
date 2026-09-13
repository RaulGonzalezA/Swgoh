using Swgoh.Application.Gac;
using Swgoh.Application.Players;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacCounterSuggestionEnricherTests
{
    [Fact]
    public void Enrich_WhenObservedCounterTeamIsOwned_ReplacesHeuristicWithExactTeam()
    {
        PlayerRosterUnit leader = Unit("ATK", "Attack Leader", 55_000);
        PlayerRosterUnit member1 = Unit("ATK2", "Attack Two", 45_000);
        PlayerRosterUnit member2 = Unit("ATK3", "Attack Three", 44_000);
        GacBattleUnit threat = new("DEF", "Defense Leader", 60_000, 9, 6, 1, false, true);
        var heuristic = new GacBattleCounterSuggestion(
            threat,
            [ToBattleUnit(leader)],
            "Low",
            "RosterStrengthHeuristic",
            "fallback",
            true);
        var plan = new CurrentGacBattlePlan(
            new GacBattleRosterComparison(1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1),
            [new GacBattleThreat(threat, 250, "Critical", "GalacticLegend", "test")],
            [],
            [],
            [heuristic],
            []);
        GacCounterStatistics[] statistics =
        [
            new(
                false,
                "DEF",
                ["DEF2", "DEF3"],
                "ATK",
                ["ATK2", "ATK3"],
                20,
                17,
                85m,
                15,
                75m,
                56.8m,
                1.1m,
                12,
                DateTimeOffset.Parse("2026-07-01T18:00:00Z"),
                DateTimeOffset.Parse("2026-09-01T18:00:00Z"))
        ];

        CurrentGacBattlePlan result = GacCounterSuggestionEnricher.Enrich(
            plan,
            GacFormat.ThreeVsThree,
            statistics,
            [leader, member1, member2],
            []);

        GacBattleCounterSuggestion counter = Assert.Single(result.CounterSuggestions);
        Assert.Equal("GlobalHistoricalCounterData", counter.Source);
        Assert.Equal("High", counter.Confidence);
        Assert.Equal(20, counter.Uses);
        Assert.Equal(85m, counter.WinRate);
        Assert.Equal(75m, counter.OneShotRate);
        Assert.Equal(56.8m, counter.AverageBanners);
        Assert.Equal(12, counter.PlayersObserved);
        Assert.Equal(["ATK", "ATK2", "ATK3"], counter.RecommendedTeam!.Select(unit => unit.DefinitionId));
    }

    [Fact]
    public void Enrich_WhenPlayerDoesNotOwnFullObservedTeam_KeepsHeuristic()
    {
        PlayerRosterUnit leader = Unit("ATK", "Attack Leader", 55_000);
        GacBattleUnit threat = new("DEF", "Defense Leader", 60_000, 9, 6, 1, false, true);
        var heuristic = new GacBattleCounterSuggestion(
            threat,
            [ToBattleUnit(leader)],
            "Low",
            "RosterStrengthHeuristic",
            "fallback",
            true);
        var plan = new CurrentGacBattlePlan(
            new GacBattleRosterComparison(1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1),
            [],
            [],
            [],
            [heuristic],
            []);
        GacCounterStatistics[] statistics =
        [
            new(
                false,
                "DEF",
                ["DEF2", "DEF3"],
                "ATK",
                ["MISSING1", "MISSING2"],
                50,
                49,
                98m,
                48,
                96m,
                58m,
                1m,
                20,
                DateTimeOffset.UtcNow.AddMonths(-2),
                DateTimeOffset.UtcNow)
        ];

        CurrentGacBattlePlan result = GacCounterSuggestionEnricher.Enrich(
            plan,
            GacFormat.ThreeVsThree,
            statistics,
            [leader],
            []);

        GacBattleCounterSuggestion counter = Assert.Single(result.CounterSuggestions);
        Assert.Equal("RosterStrengthHeuristic", counter.Source);
        Assert.Null(counter.RecommendedTeam);
    }

    private static PlayerRosterUnit Unit(string definitionId, string name, long gp) => new(
        definitionId.ToLowerInvariant(),
        definitionId,
        name,
        null,
        null,
        [],
        [],
        85,
        7,
        13,
        9,
        6,
        gp,
        false,
        6,
        0);

    private static GacBattleUnit ToBattleUnit(PlayerRosterUnit unit) => new(
        unit.DefinitionId,
        unit.Name,
        unit.GalacticPower,
        unit.RelicTier,
        unit.ZetaCount,
        unit.OmicronCount,
        unit.IsShip,
        false);
}
