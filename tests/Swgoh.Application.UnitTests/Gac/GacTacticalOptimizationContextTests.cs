using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacTacticalOptimizationContextTests
{
    private const long PlayerAllyCode = 123456789;
    private const long OpponentAllyCode = 987654321;

    [Fact]
    public void Evaluate_WhenAttackIsFasterAndBetterModded_AddsPositiveAdjustment()
    {
        GacTeamPresetDetails attack = Preset("Fast attack", ["A", "B", "C"]);
        GacVisibleDefenseDetails defense = Defense(["X", "Y", "Z"]);
        PlayerProfile player = Profile(
            PlayerAllyCode,
            [
                Unit("A", 360m, 90m), Unit("B", 340m, 80m), Unit("C", 320m, 70m)
            ]);
        PlayerProfile opponent = Profile(
            OpponentAllyCode,
            [
                Unit("X", 300m, 45m), Unit("Y", 290m, 40m), Unit("Z", 280m, 35m)
            ]);
        GacTacticalOptimizationContext context = GacTacticalOptimizationContext.From(player, opponent);

        GacTacticalEvaluation result = context.Evaluate(defense, attack, requiresDatacronVerification: false);

        Assert.True(result.Adjustment > 0m);
        Assert.Equal(340m, result.TeamAverageSpeed);
        Assert.Equal(290m, result.DefenseAverageSpeed);
        Assert.Equal(80m, result.TeamModSpeedBonus);
        Assert.Equal(40m, result.DefenseModSpeedBonus);
        Assert.Equal("NotRequired", result.DatacronStatus);
        Assert.Contains("velocidad", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenDatacronIsRequiredAndNoneIsEligible_PenalizesAndMarksRisk()
    {
        GacTeamPresetDetails attack = Preset("Counter", ["A", "B", "C"]);
        GacVisibleDefenseDetails defense = Defense(["X", "Y", "Z"]);
        PlayerProfile player = Profile(
            PlayerAllyCode,
            [Unit("A", 300m, 50m, relic: 7), Unit("B", 300m, 50m, relic: 7), Unit("C", 300m, 50m, relic: 7)]);
        PlayerProfile opponent = Profile(
            OpponentAllyCode,
            [Unit("X", 300m, 50m), Unit("Y", 300m, 50m), Unit("Z", 300m, 50m)]);
        GacTacticalOptimizationContext context = GacTacticalOptimizationContext.From(player, opponent);

        GacTacticalEvaluation result = context.Evaluate(defense, attack, requiresDatacronVerification: true);

        Assert.Equal("NoCandidate", result.DatacronStatus);
        Assert.True(result.Adjustment < 0m);
        Assert.Contains("no hay datacron", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenEligibleLevelNineDatacronExists_MarksItAsUnverifiedCandidate()
    {
        GacTeamPresetDetails attack = Preset("Counter", ["A", "B", "C"]);
        GacVisibleDefenseDetails defense = Defense(["X", "Y", "Z"]);
        PlayerDatacron datacron = new(
            "dc-1",
            "set-1",
            "template-1",
            9,
            false,
            [new PlayerDatacronAffix("ability", null, null, 7, ["TEST"])]);
        PlayerProfile player = Profile(
            PlayerAllyCode,
            [Unit("A", 300m, 50m, relic: 7), Unit("B", 300m, 50m, relic: 7), Unit("C", 300m, 50m, relic: 7)],
            [datacron]);
        PlayerProfile opponent = Profile(
            OpponentAllyCode,
            [Unit("X", 300m, 50m), Unit("Y", 300m, 50m), Unit("Z", 300m, 50m)]);
        GacTacticalOptimizationContext context = GacTacticalOptimizationContext.From(player, opponent);

        GacTacticalEvaluation result = context.Evaluate(defense, attack, requiresDatacronVerification: true);

        Assert.Equal("Level9AvailableUnverified", result.DatacronStatus);
        Assert.True(result.Adjustment > 0m);
        Assert.Contains("pendiente de verificar", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static PlayerProfile Profile(
        long allyCode,
        IReadOnlyCollection<RosterUnit> roster,
        IReadOnlyCollection<PlayerDatacron>? datacrons = null) =>
        PlayerProfile.Import(
            allyCode,
            $"player-{allyCode}",
            allyCode == PlayerAllyCode ? "Player" : "Opponent",
            null,
            null,
            85,
            roster.Sum(unit => unit.GalacticPower),
            DateTimeOffset.UtcNow,
            roster,
            datacrons);

    private static RosterUnit Unit(
        string id,
        decimal speed,
        decimal modSpeed,
        int relic = 7) => new(
            id,
            id,
            85,
            7,
            13,
            relic,
            6,
            30_000,
            false,
            1,
            0,
            new RosterUnitStats(
                Health: 100_000m,
                Protection: 80_000m,
                Speed: speed,
                PhysicalDamage: 10_000m,
                SpecialDamage: 8_000m),
            new RosterModSummary(6, 6, 4, 1, modSpeed));

    private static GacTeamPresetDetails Preset(string name, IReadOnlyCollection<string> ids)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(UnitDetails)];
        return new GacTeamPresetDetails(
            Guid.NewGuid(),
            PlayerAllyCode,
            name,
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Offense,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            DateTimeOffset.UtcNow);
    }

    private static GacVisibleDefenseDetails Defense(IReadOnlyCollection<string> ids)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(UnitDetails)];
        return new GacVisibleDefenseDetails(
            Guid.NewGuid(),
            "Sur frontal",
            null,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            false);
    }

    private static GacPlannerUnitDetails UnitDetails(string id) => new(
        id,
        id,
        null,
        false,
        30_000,
        7,
        1,
        0);
}
