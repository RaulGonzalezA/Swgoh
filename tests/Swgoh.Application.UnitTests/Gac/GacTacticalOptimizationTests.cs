using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacTacticalOptimizationTests
{
    private const long PlayerAllyCode = 123456789;
    private const long OpponentAllyCode = 987654321;

    [Fact]
    public void Optimize_WhenTeamsHaveSamePower_PrefersBetterTacticalReadiness()
    {
        Guid defenseId = Guid.NewGuid();
        GacTeamPresetDetails fast = Preset("Fast", ["A", "B", "C"]);
        GacTeamPresetDetails slow = Preset("Slow", ["D", "E", "F"]);
        GacVisibleDefenseDetails defense = Defense(defenseId, ["X", "Y", "Z"]);
        GacPlannerState state = State([fast, slow], defense);
        PlayerProfile player = Profile(
            PlayerAllyCode,
            [
                Unit("A", 350, 110), Unit("B", 340, 105), Unit("C", 330, 100),
                Unit("D", 220, 35), Unit("E", 215, 30), Unit("F", 210, 25)
            ]);
        PlayerProfile opponent = Profile(
            OpponentAllyCode,
            [Unit("X", 275, 65), Unit("Y", 270, 60), Unit("Z", 265, 55)]);
        GacTacticalOptimizationContext context = GacTacticalOptimizationContext.From(player, opponent);

        GacAttackOptimizationResult result = GacAttackPlanOptimizerService.Optimize(
            state,
            GacAttackOptimizationMode.RebuildPlanned,
            context);

        GacAttackOptimizationRecommendation recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(fast.Id, recommendation.TeamPresetId);
        Assert.True(recommendation.TacticalAdjustment > 0m);
        Assert.True(recommendation.TeamAverageSpeed > recommendation.DefenseAverageSpeed);
        Assert.True(recommendation.TeamModSpeedBonus > recommendation.DefenseModSpeedBonus);
    }

    [Fact]
    public void Optimize_WhenCounterRequiresDatacron_PenalizesMissingAndRewardsEligibleLevelNine()
    {
        Guid defenseId = Guid.NewGuid();
        GacTeamPresetDetails team = Preset("Counter", ["A", "B", "C"]);
        GacVisibleDefenseDetails defense = Defense(defenseId, ["X", "Y", "Z"]);
        var hint = new GacPlannerCounterHint(
            defenseId,
            "Enemy",
            "High",
            "ObservedCounterStatistics",
            "Datacron-sensitive matchup.",
            true,
            team.Id,
            team.Squad.AllUnits,
            50,
            0.9m,
            0.85m,
            55m,
            30);
        GacPlannerState state = State([team], defense, hint);
        RosterUnit[] ownUnits = [Unit("A", 300, 80), Unit("B", 295, 75), Unit("C", 290, 70)];
        PlayerProfile opponent = Profile(
            OpponentAllyCode,
            [Unit("X", 285, 65), Unit("Y", 280, 60), Unit("Z", 275, 55)]);

        GacAttackOptimizationRecommendation withoutDatacron = Assert.Single(
            GacAttackPlanOptimizerService.Optimize(
                state,
                GacAttackOptimizationMode.RebuildPlanned,
                GacTacticalOptimizationContext.From(Profile(PlayerAllyCode, ownUnits), opponent))
            .Recommendations);
        PlayerDatacron levelNine = new(
            "dc-9",
            "set",
            "template",
            9,
            false,
            [new PlayerDatacronAffix("ability", null, null, 7, [])]);
        GacAttackOptimizationRecommendation withDatacron = Assert.Single(
            GacAttackPlanOptimizerService.Optimize(
                state,
                GacAttackOptimizationMode.RebuildPlanned,
                GacTacticalOptimizationContext.From(Profile(PlayerAllyCode, ownUnits, [levelNine]), opponent))
            .Recommendations);

        Assert.Equal("NoCandidate", withoutDatacron.DatacronStatus);
        Assert.Equal("Level9AvailableUnverified", withDatacron.DatacronStatus);
        Assert.True(withDatacron.Score > withoutDatacron.Score);
        Assert.True(withDatacron.TacticalAdjustment > withoutDatacron.TacticalAdjustment);
    }

    private static GacPlannerState State(
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        GacVisibleDefenseDetails defense,
        GacPlannerCounterHint? hint = null)
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
            hint is null ? [] : [hint],
            DateTimeOffset.UtcNow);
        return new GacPlannerState(opponent, presets, plan);
    }

    private static GacTeamPresetDetails Preset(string name, IReadOnlyCollection<string> ids)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(id => PlannerUnit(id))];
        return new GacTeamPresetDetails(
            Guid.NewGuid(),
            PlayerAllyCode,
            name,
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Offense,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            DateTimeOffset.UtcNow);
    }

    private static GacVisibleDefenseDetails Defense(Guid id, IReadOnlyCollection<string> ids)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(PlannerUnit)];
        return new GacVisibleDefenseDetails(
            id,
            "Sur frontal",
            null,
            new GacPlannerSquadDetails(units[0] with { Name = "Enemy" }, [.. units.Skip(1)], false),
            false);
    }

    private static GacPlannerUnitDetails PlannerUnit(string id) => new(
        id,
        id,
        null,
        false,
        25_000,
        7,
        1,
        0);

    private static PlayerProfile Profile(
        long allyCode,
        IReadOnlyCollection<RosterUnit> roster,
        IReadOnlyCollection<PlayerDatacron>? datacrons = null) => PlayerProfile.Import(
            allyCode,
            $"player-{allyCode}",
            allyCode == PlayerAllyCode ? "Player" : "Opponent",
            null,
            null,
            85,
            roster.Sum(unit => unit.GalacticPower),
            DateTimeOffset.UtcNow,
            roster,
            datacrons ?? []);

    private static RosterUnit Unit(string id, decimal speed, decimal modSpeed) => new(
        id,
        id,
        85,
        7,
        13,
        7,
        6,
        25_000,
        false,
        1,
        0,
        new RosterUnitStats(
            Health: 100_000,
            Protection: 80_000,
            Speed: speed,
            PhysicalDamage: 10_000),
        new RosterModSummary(6, 6, 4, 1, modSpeed));
}
