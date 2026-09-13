using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Domain.UnitTests.Gac;

public sealed class GacPlanningTests
{
    [Fact]
    public void PlannerSquad_CreateFiveVsFive_RequiresFiveCharacters()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => GacPlannerSquad.Create(
            GacFormat.FiveVsFive,
            "LEADER",
            ["A", "B", "C"],
            isFleet: false));

        Assert.Contains("exactly 5 units", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamPreset_Create_WithDuplicateUnits_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => GacPlannerSquad.Create(
            GacFormat.ThreeVsThree,
            "LEADER",
            ["ALLY", "LEADER"],
            isFleet: false));
    }

    [Fact]
    public void RoundPlan_Replace_RejectsAttackForUnknownDefense()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GacRoundPlan plan = GacRoundPlan.Create(
            123456789,
            987654321,
            "event",
            "instance",
            1,
            GacFormat.ThreeVsThree,
            GacLeague.Kyber,
            now);
        GacAttackAssignment attack = GacAttackAssignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            GacAttackPlanStatus.Planned,
            null);

        Assert.Throws<ArgumentException>(() => plan.Replace([], [], [attack], now));
    }

    [Fact]
    public void RoundPlan_Replace_AllowsSequentialAttemptsForSameDefense()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid defenseId = Guid.NewGuid();
        GacRoundPlan plan = GacRoundPlan.Create(
            123456789,
            987654321,
            "event",
            "instance",
            2,
            GacFormat.ThreeVsThree,
            GacLeague.Kyber,
            now);
        GacVisibleDefense defense = GacVisibleDefense.Create(
            defenseId,
            "south-front",
            "Malgus",
            GacPlannerSquad.Create(GacFormat.ThreeVsThree, "MALGUS", ["DR", "BSF"], isFleet: false));
        GacAttackAssignment first = GacAttackAssignment.Create(
            Guid.NewGuid(),
            defenseId,
            Guid.NewGuid(),
            1,
            GacAttackPlanStatus.Failed,
            null);
        GacAttackAssignment second = GacAttackAssignment.Create(
            Guid.NewGuid(),
            defenseId,
            Guid.NewGuid(),
            2,
            GacAttackPlanStatus.Planned,
            null);

        plan.Replace([], [defense], [first, second], now.AddMinutes(1));

        Assert.Equal(2, plan.Attacks.Count);
        Assert.Equal(GacAttackPlanStatus.Planned, plan.Attacks[1].Status);
    }

    [Fact]
    public void RoundPlan_Replace_RejectsDuplicateAttemptNumberForDefense()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid defenseId = Guid.NewGuid();
        GacRoundPlan plan = GacRoundPlan.Create(
            123456789,
            987654321,
            "event",
            "instance",
            1,
            GacFormat.ThreeVsThree,
            GacLeague.Aurodium,
            now);
        GacVisibleDefense defense = GacVisibleDefense.Create(
            defenseId,
            "north-front",
            null,
            GacPlannerSquad.Create(GacFormat.ThreeVsThree, "LEADER", ["A", "B"], isFleet: false));
        GacAttackAssignment first = GacAttackAssignment.Create(
            Guid.NewGuid(),
            defenseId,
            Guid.NewGuid(),
            1,
            GacAttackPlanStatus.Failed,
            null);
        GacAttackAssignment duplicate = GacAttackAssignment.Create(
            Guid.NewGuid(),
            defenseId,
            Guid.NewGuid(),
            1,
            GacAttackPlanStatus.Planned,
            null);

        Assert.Throws<ArgumentException>(() => plan.Replace([], [defense], [first, duplicate], now));
    }
}
