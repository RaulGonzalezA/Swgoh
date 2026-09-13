using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacPersonalLearningOptimizerTests
{
    private const long PlayerAllyCode = 123456789;
    private const long OpponentAllyCode = 987654321;

    [Fact]
    public void Optimize_WhenCandidatesAreOtherwiseEquivalent_PrefersPersonallyProvenCounter()
    {
        GacTeamPresetDetails neutral = Preset("Neutral", ["D", "E", "F"]);
        GacTeamPresetDetails proven = Preset("Proven", ["A", "B", "C"]);
        GacVisibleDefenseDetails defense = Defense(["X", "Y", "Z"]);
        GacPlannerState state = State([neutral, proven], defense);
        string matchupKey = GacPersonalBattleObservation.BuildMatchupKey(
            GacFormat.ThreeVsThree,
            false,
            proven.Squad.AllUnits.Select(unit => unit.DefinitionId),
            defense.Squad.AllUnits.Select(unit => unit.DefinitionId));
        var stats = new GacPersonalMatchupStatistics(
            matchupKey,
            false,
            [.. proven.Squad.AllUnits.Select(unit => unit.DefinitionId)],
            [.. defense.Squad.AllUnits.Select(unit => unit.DefinitionId)],
            8,
            8,
            1m,
            1m,
            56m,
            DateTimeOffset.UtcNow);
        GacPersonalLearningContext personal = GacPersonalLearningContext.From([stats]);

        GacAttackOptimizationResult result = GacAttackPlanOptimizerService.Optimize(
            state,
            GacAttackOptimizationMode.RebuildPlanned,
            GacTacticalOptimizationContext.Empty,
            personal);

        GacAttackOptimizationRecommendation recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(proven.Id, recommendation.TeamPresetId);
        Assert.Equal(8, recommendation.PersonalSamples);
        Assert.Equal(8, recommendation.PersonalWins);
        Assert.True(recommendation.PersonalAdjustment > 0m);
        Assert.Equal(1m, recommendation.PersonalWinRate);
    }

    private static GacPlannerState State(
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        GacVisibleDefenseDetails defense)
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
            [defense],
            [],
            [],
            [],
            DateTimeOffset.UtcNow);
        return new GacPlannerState(opponent, presets, plan);
    }

    private static GacTeamPresetDetails Preset(string name, string[] ids)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(Unit)];
        return new GacTeamPresetDetails(
            Guid.NewGuid(),
            PlayerAllyCode,
            name,
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Offense,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            DateTimeOffset.UtcNow);
    }

    private static GacVisibleDefenseDetails Defense(string[] ids)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(Unit)];
        return new GacVisibleDefenseDetails(
            Guid.NewGuid(),
            "Sur frontal",
            null,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            false);
    }

    private static GacPlannerUnitDetails Unit(string id) => new(
        id,
        id,
        null,
        false,
        20_000,
        7,
        1,
        0);
}
