using Swgoh.Domain.Conquest;

using Xunit;

namespace Swgoh.Domain.UnitTests.Conquest;

public sealed class ConquestPlanTests
{
    [Fact]
    public void Create_FactionFeatWithFullTeam_IsValid()
    {
        ConquestFeatRule rule = ConquestFeatRule.Create(
            ConquestFeatRuleType.Faction,
            "JEDI",
            [],
            minimumMatchingUnits: 5);

        ConquestFeat feat = ConquestFeat.Create(
            Guid.NewGuid(),
            "Gana con Jedi",
            ConquestFeatScope.Sector,
            sector: 2,
            points: 10,
            target: 5,
            progress: 2,
            expectedProgressPerBattle: 1,
            rule);

        Assert.Equal(3, feat.Remaining);
        Assert.False(feat.IsComplete);
        Assert.Equal(5, feat.Rule.MinimumMatchingUnits);
    }

    [Fact]
    public void Create_GlobalFeatRejectsSector()
    {
        ConquestFeatRule rule = ConquestFeatRule.Create(
            ConquestFeatRuleType.AnyCharacter,
            null,
            [],
            1);

        Assert.Throws<ArgumentException>(() => ConquestFeat.Create(
            Guid.NewGuid(),
            "Global",
            ConquestFeatScope.Global,
            sector: 1,
            points: 5,
            target: 10,
            progress: 0,
            expectedProgressPerBattle: 1,
            rule));
    }

    [Fact]
    public void Create_SpecificUnitsRejectsImpossibleMinimum()
    {
        Assert.Throws<ArgumentException>(() => ConquestFeatRule.Create(
            ConquestFeatRuleType.SpecificUnits,
            null,
            ["A", "B"],
            minimumMatchingUnits: 3));
    }

    [Fact]
    public void Create_StaminaState_UsesConfiguredAndDefaultValues()
    {
        DateTimeOffset now = new(2026, 9, 13, 18, 0, 0, TimeSpan.Zero);
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "event",
            "Conquista",
            ConquestDifficulty.Hard,
            [],
            now,
            staminaCostPerBattle: 10,
            reserveFloorPercent: 40,
            stamina: [ConquestUnitStamina.Create("LOW", 30)]);

        Assert.Equal(30, plan.GetCurrentStamina("LOW"));
        Assert.Equal(100, plan.GetCurrentStamina("UNTRACKED"));
        Assert.Equal(10, plan.StaminaCostPerBattle);
        Assert.Equal(40, plan.ReserveFloorPercent);
    }

    [Fact]
    public void Create_StaminaRejectsValuesOutsidePercentRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConquestUnitStamina.Create("UNIT", 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConquestUnitStamina.Create("UNIT", -1));
    }

    [Fact]
    public void DailyGoal_DefaultsDoNotLimitEnergyOrSetRewardTarget()
    {
        DateTimeOffset now = new(2026, 9, 14, 5, 0, 0, TimeSpan.Zero);
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "event-goal-defaults",
            "Conquista",
            ConquestDifficulty.Hard,
            [],
            now);

        Assert.Null(plan.AvailableEnergy);
        Assert.Equal(ConquestPlan.DefaultEnergyCostPerBattle, plan.EnergyCostPerBattle);
        Assert.Equal(0, plan.CurrentRewardPoints);
        Assert.Null(plan.TargetRewardPoints);
        Assert.Null(plan.RewardTargetName);
        Assert.False(plan.HasRewardTarget);
        Assert.False(plan.RewardTargetReached);
    }

    [Fact]
    public void ReplaceDailyGoal_StoresEnergyAndRewardTarget()
    {
        DateTimeOffset now = new(2026, 9, 14, 5, 0, 0, TimeSpan.Zero);
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "event-goal",
            "Conquista",
            ConquestDifficulty.Hard,
            [],
            now);

        plan.ReplaceDailyGoal(
            availableEnergy: 120,
            energyCostPerBattle: 20,
            currentRewardPoints: 520,
            targetRewardPoints: 530,
            rewardTargetName: "Caja roja",
            now.AddMinutes(1));

        Assert.Equal(120, plan.AvailableEnergy);
        Assert.Equal(20, plan.EnergyCostPerBattle);
        Assert.Equal(520, plan.CurrentRewardPoints);
        Assert.Equal(530, plan.TargetRewardPoints);
        Assert.Equal("Caja roja", plan.RewardTargetName);
        Assert.True(plan.HasRewardTarget);
        Assert.False(plan.RewardTargetReached);
    }

    [Fact]
    public void ReplaceDailyGoal_RejectsInvalidEnergyAndPoints()
    {
        DateTimeOffset now = new(2026, 9, 14, 5, 0, 0, TimeSpan.Zero);
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "event-goal-invalid",
            "Conquista",
            ConquestDifficulty.Hard,
            [],
            now);

        Assert.Throws<ArgumentOutOfRangeException>(() => plan.ReplaceDailyGoal(
            -1,
            20,
            0,
            null,
            null,
            now));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.ReplaceDailyGoal(
            100,
            0,
            0,
            null,
            null,
            now));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.ReplaceDailyGoal(
            100,
            20,
            -1,
            null,
            null,
            now));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.ReplaceDailyGoal(
            100,
            20,
            0,
            -1,
            null,
            now));
    }
}
