using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacCounterEngineTests
{
    private const long PlayerAllyCode = 123456789;
    private const long OpponentAllyCode = 987654321;

    [Fact]
    public void Optimize_ExposesRankedCounterEngineAlternativesWithRiskAndExpectedOutcome()
    {
        Guid defenseId = Guid.NewGuid();
        GacTeamPresetDetails observed = Preset("Observed counter", ["A", "B", "C"], 25_000);
        GacTeamPresetDetails fallback = Preset("Fallback", ["D", "E", "F"], 32_000);
        GacVisibleDefenseDetails defense = Defense(defenseId, "Enemy", ["X", "Y", "Z"], 24_000);
        var hint = new GacPlannerCounterHint(
            defenseId,
            "Enemy",
            "High",
            "ObservedCounterStatistics",
            "Observed exact counter.",
            false,
            observed.Id,
            observed.Squad.AllUnits,
            25,
            0.92m,
            0.88m,
            56.4m,
            18);
        GacPlannerState state = State([observed, fallback], [defense], [hint]);

        GacAttackOptimizationResult result = GacAttackPlanOptimizerService.Optimize(
            state,
            GacAttackOptimizationMode.RebuildPlanned);

        GacCounterDefenseAnalysis analysis = Assert.Single(result.CounterEngine);
        Assert.Equal(defenseId, analysis.DefenseId);
        Assert.Equal(2, analysis.Candidates.Count);

        GacCounterCandidateAnalysis best = analysis.Candidates.OrderBy(candidate => candidate.Rank).First();
        Assert.Equal(observed.Id, best.TeamPresetId);
        Assert.True(best.EstimatedWinProbability >= 85m);
        Assert.Equal(56.4m, best.ExpectedBanners);
        Assert.Equal("Low", best.Risk);
        Assert.Equal("Low", best.TimeoutRisk);
        Assert.Equal("Histórico observado", best.Evidence);

        GacAttackOptimizationRecommendation recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(best.TeamPresetId, recommendation.TeamPresetId);
        Assert.Equal(best.EstimatedWinProbability, recommendation.EstimatedWinProbability);
        Assert.Equal(best.Risk, recommendation.Risk);
        Assert.Equal(best.TimeoutRisk, recommendation.TimeoutRisk);
    }

    [Fact]
    public void Optimize_LimitsCounterEngineToFiveAlternativesPerDefense()
    {
        Guid defenseId = Guid.NewGuid();
        GacVisibleDefenseDetails defense = Defense(defenseId, "Enemy", ["X", "Y", "Z"], 20_000);
        GacTeamPresetDetails[] presets =
        [
            Preset("One", ["A1", "A2", "A3"], 22_000),
            Preset("Two", ["B1", "B2", "B3"], 23_000),
            Preset("Three", ["C1", "C2", "C3"], 24_000),
            Preset("Four", ["D1", "D2", "D3"], 25_000),
            Preset("Five", ["E1", "E2", "E3"], 26_000),
            Preset("Six", ["F1", "F2", "F3"], 27_000)
        ];
        GacPlannerState state = State(presets, [defense], []);

        GacAttackOptimizationResult result = GacAttackPlanOptimizerService.Optimize(
            state,
            GacAttackOptimizationMode.RebuildPlanned);

        GacCounterDefenseAnalysis analysis = Assert.Single(result.CounterEngine);
        Assert.Equal(5, analysis.Candidates.Count);
        Assert.Equal([1, 2, 3, 4, 5], analysis.Candidates.Select(candidate => candidate.Rank));
    }

    private static GacPlannerState State(
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        IReadOnlyCollection<GacVisibleDefenseDetails> defenses,
        IReadOnlyCollection<GacPlannerCounterHint> hints)
    {
        var opponent = new CurrentGacOpponent(
            PlayerAllyCode,
            OpponentAllyCode,
            "Opponent",
            "opponent-id",
            GacLeague.Kyber,
            GacFormat.ThreeVsThree,
            "event",
            "event-instance",
            "bracket",
            1,
            "test",
            "test");
        var plan = new GacRoundPlanDetails(
            GacRoundPlan.BuildId(PlayerAllyCode, "event-instance", 1),
            PlayerAllyCode,
            OpponentAllyCode,
            "Opponent",
            "event",
            "event-instance",
            1,
            GacFormat.ThreeVsThree,
            GacLeague.Kyber,
            [],
            defenses,
            [],
            [],
            hints,
            DateTimeOffset.UtcNow);
        return new GacPlannerState(opponent, presets, plan);
    }

    private static GacTeamPresetDetails Preset(
        string name,
        IReadOnlyCollection<string> ids,
        long unitPower)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(id => Unit(id, unitPower))];
        return new GacTeamPresetDetails(
            Guid.NewGuid(),
            PlayerAllyCode,
            name,
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Offense,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            DateTimeOffset.UtcNow);
    }

    private static GacVisibleDefenseDetails Defense(
        Guid id,
        string name,
        IReadOnlyCollection<string> ids,
        long unitPower)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(unitId => Unit(unitId, unitPower))];
        units[0] = units[0] with { Name = name };
        return new GacVisibleDefenseDetails(
            id,
            "Sur frontal",
            null,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            false);
    }

    private static GacPlannerUnitDetails Unit(string id, long power) => new(
        id,
        id,
        null,
        false,
        power,
        7,
        1,
        0);
}
