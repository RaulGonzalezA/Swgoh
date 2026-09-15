using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacDatacronAssignmentTests
{
    private const long PlayerAllyCode = 123456789;
    private const long OpponentAllyCode = 987654321;
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 17, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Replace_RejectsDatacronUsedByDefenseAndPlannedAttack()
    {
        GacRoundPlan plan = CreatePlan();
        GacVisibleDefense visible = VisibleDefense();
        const string datacronId = "dc-exclusive";

        ArgumentException exception = Assert.Throws<ArgumentException>(() => plan.Replace(
            [GacOwnDefenseAssignment.Create(Guid.NewGuid(), "Sur frontal", Guid.NewGuid(), datacronId)],
            [visible],
            [GacAttackAssignment.Create(
                Guid.NewGuid(),
                visible.Id,
                Guid.NewGuid(),
                1,
                GacAttackPlanStatus.Planned,
                null,
                datacronId)],
            Now.AddMinutes(1)));

        Assert.Contains("only be assigned once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(GacAttackPlanStatus.Won)]
    [InlineData(GacAttackPlanStatus.Failed)]
    [InlineData(GacAttackPlanStatus.Cancelled)]
    public void Replace_ReleasesDatacronWhenAttackIsNoLongerPlanned(GacAttackPlanStatus status)
    {
        GacRoundPlan plan = CreatePlan();
        GacVisibleDefense visible = VisibleDefense();
        const string datacronId = "dc-released";

        plan.Replace(
            [GacOwnDefenseAssignment.Create(Guid.NewGuid(), "Sur frontal", Guid.NewGuid(), datacronId)],
            [visible],
            [GacAttackAssignment.Create(
                Guid.NewGuid(),
                visible.Id,
                Guid.NewGuid(),
                1,
                status,
                null,
                datacronId)],
            Now.AddMinutes(1));

        Assert.Equal(datacronId, Assert.Single(plan.OwnDefenses).DatacronId);
        Assert.Equal(datacronId, Assert.Single(plan.Attacks).DatacronId);
    }

    private static GacRoundPlan CreatePlan() => GacRoundPlan.Create(
        PlayerAllyCode,
        OpponentAllyCode,
        "event",
        "instance",
        1,
        GacFormat.FiveVsFive,
        GacLeague.Kyber,
        Now);

    private static GacVisibleDefense VisibleDefense() => GacVisibleDefense.Create(
        Guid.NewGuid(),
        "Sur frontal",
        "Rival",
        GacPlannerSquad.Create(
            GacFormat.FiveVsFive,
            "L",
            ["A", "B", "C", "D"],
            isFleet: false));
}
