using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacPersonalizedAttackOptimizerTests
{
    [Fact]
    public void Optimize_PrefersTeamWithStrongPersonalExactHistory()
    {
        GacTeamPresetDetails learned = Preset("Learned", ["A", "B", "C"]);
        GacTeamPresetDetails neutral = Preset("Neutral", ["D", "E", "F"]);
        GacVisibleDefenseDetails defense = Defense(["X", "Y", "Z"]);
        string[] attackIds = [.. learned.Squad.AllUnits.Select(unit => unit.DefinitionId)];
        string[] defenseIds = [.. defense.Squad.AllUnits.Select(unit => unit.DefinitionId)];
        var personal = GacPersonalLearningContext.From(
            [new GacPersonalMatchupStatistics(
                GacPersonalBattleObservation.BuildMatchupKey(GacFormat.ThreeVsThree, false, attackIds, defenseIds),
                false,
                attackIds,
                defenseIds,
                8,
                8,
                1m,
                0.875m,
                null,
                DateTimeOffset.UtcNow)]);

        GacAttackOptimizationResult result = GacAttackPlanOptimizerService.Optimize(
            State([learned, neutral], defense),
            GacAttackOptimizationMode.RebuildPlanned,
            GacTacticalOptimizationContext.Empty,
            personal);

        GacAttackOptimizationRecommendation recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(learned.Id, recommendation.TeamPresetId);
        Assert.Equal(8, recommendation.PersonalSamples);
        Assert.Equal(4m, recommendation.PersonalAdjustment);
    }

    private static GacPlannerState State(
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        GacVisibleDefenseDetails defense)
    {
        var opponent = new CurrentGacOpponent(
            123_456_789,
            987_654_321,
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
            GacRoundPlan.BuildId(123_456_789, "event-instance", 1),
            123_456_789,
            987_654_321,
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
        return new(
            Guid.NewGuid(),
            123_456_789,
            name,
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Offense,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            DateTimeOffset.UtcNow);
    }

    private static GacVisibleDefenseDetails Defense(string[] ids)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(Unit)];
        return new(Guid.NewGuid(), "Sur frontal", null, new(units[0], [.. units.Skip(1)], false), false);
    }

    private static GacPlannerUnitDetails Unit(string id) => new(id, id, null, false, 20_000, 7, 1, 0);
}
