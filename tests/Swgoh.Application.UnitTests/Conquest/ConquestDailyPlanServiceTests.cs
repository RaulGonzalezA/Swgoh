using Swgoh.Application.Conquest;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Conquest;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Conquest;

public sealed class ConquestDailyPlanServiceTests
{
    [Fact]
    public async Task BuildAsync_ProjectsProgressAndStaminaAcrossBattles()
    {
        DateTimeOffset now = new(2026, 9, 14, 0, 15, 0, TimeSpan.Zero);
        ConquestFeat feat = ConquestFeat.Create(
            Guid.NewGuid(),
            "Two Jedi wins",
            ConquestFeatScope.Sector,
            1,
            10,
            2,
            0,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.Faction, "JEDI", [], 5));
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "conquest-daily",
            "Conquista",
            ConquestDifficulty.Hard,
            [feat],
            now,
            staminaCostPerBattle: 10,
            reserveFloorPercent: 40);
        PlayerProfile profile = Profile(now, ["J1", "J2", "J3", "J4", "J5"]);
        GameDataCatalog catalog = Catalog(["J1", "J2", "J3", "J4", "J5"]);
        var service = CreateService(plan, profile, catalog);

        ConquestDailyPlanResult? result = await service.BuildAsync(
            123_456_789,
            new ConquestDailyPlanRequest(6),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result!.PlannedBattles);
        Assert.Equal("AllFeatsCompleted", result.StopReason);
        Assert.Equal(1, result.ProjectedCompletedFeats);
        Assert.Equal(0, result.ProjectedRemainingFeats);
        Assert.Equal(100m, result.Steps.ElementAt(0).AverageStaminaBefore);
        Assert.Equal(90m, result.Steps.ElementAt(0).AverageStaminaAfter);
        Assert.Equal(90m, result.Steps.ElementAt(1).AverageStaminaBefore);
        Assert.Equal(80m, result.Steps.ElementAt(1).AverageStaminaAfter);
        Assert.Equal(1, result.Steps.ElementAt(0).FeatProgress.Single().AfterProgress);
        Assert.Equal(2, result.Steps.ElementAt(1).FeatProgress.Single().AfterProgress);
        Assert.True(result.Steps.ElementAt(1).FeatProgress.Single().CompletedByBattle);
        Assert.Equal(10, result.ProjectedRewardPointsGained);
        Assert.Equal(40, result.EnergySpent);
    }

    [Fact]
    public async Task BuildAsync_RotatesTeamWhenNextFeatRequiresDifferentUnits()
    {
        DateTimeOffset now = new(2026, 9, 14, 0, 20, 0, TimeSpan.Zero);
        string[] alpha = ["A1", "A2", "A3", "A4", "A5"];
        string[] beta = ["B1", "B2", "B3", "B4", "B5"];
        ConquestFeat alphaFeat = SpecificFeat("Alpha win", 20, 1, 0, 1, alpha);
        ConquestFeat betaFeat = SpecificFeat("Beta win", 10, 1, 0, 1, beta);
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "conquest-rotate",
            "Conquista",
            ConquestDifficulty.Hard,
            [alphaFeat, betaFeat],
            now);
        PlayerProfile profile = Profile(now, [.. alpha, .. beta]);
        GameDataCatalog catalog = Catalog([.. alpha, .. beta]);
        var service = CreateService(plan, profile, catalog);

        ConquestDailyPlanResult? result = await service.BuildAsync(
            123_456_789,
            new ConquestDailyPlanRequest(4),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result!.PlannedBattles);
        Assert.False(result.Steps.First().ChangesTeamFromPrevious);
        Assert.True(result.Steps.Last().ChangesTeamFromPrevious);
        Assert.All(result.Steps.First().Team, unit => Assert.Contains(unit.DefinitionId, alpha));
        Assert.All(result.Steps.Last().Team, unit => Assert.Contains(unit.DefinitionId, beta));
        Assert.Equal(2, result.ProjectedCompletedFeats);
        Assert.Equal("AllFeatsCompleted", result.StopReason);
    }

    [Fact]
    public async Task BuildAsync_StopsAsSoonAsRewardTargetIsReached()
    {
        DateTimeOffset now = new(2026, 9, 14, 5, 30, 0, TimeSpan.Zero);
        string[] alpha = ["A1", "A2", "A3", "A4", "A5"];
        string[] beta = ["B1", "B2", "B3", "B4", "B5"];
        ConquestFeat closesTarget = SpecificFeat("Close crate", 10, 1, 0, 1, alpha);
        ConquestFeat valuableButPartial = SpecificFeat("Long valuable feat", 100, 2, 0, 1, beta);
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "conquest-target",
            "Conquista",
            ConquestDifficulty.Hard,
            [closesTarget, valuableButPartial],
            now);
        plan.ReplaceDailyGoal(
            availableEnergy: 100,
            energyCostPerBattle: 20,
            currentRewardPoints: 520,
            targetRewardPoints: 530,
            rewardTargetName: "Caja roja",
            now.AddMinutes(1));
        PlayerProfile profile = Profile(now, [.. alpha, .. beta]);
        GameDataCatalog catalog = Catalog([.. alpha, .. beta]);
        var service = CreateService(plan, profile, catalog);

        ConquestDailyPlanResult? result = await service.BuildAsync(
            123_456_789,
            new ConquestDailyPlanRequest(6),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(1, result!.PlannedBattles);
        Assert.Equal("RewardTargetReached", result.StopReason);
        Assert.True(result.RewardTargetReached);
        Assert.Equal(20, result.EnergySpent);
        Assert.Equal(80, result.EnergyRemaining);
        Assert.Equal(520, result.StartingRewardPoints);
        Assert.Equal(530, result.ProjectedRewardPoints);
        Assert.Equal(10, result.ProjectedRewardPointsGained);
        Assert.Equal(0.5m, result.RewardPointsPerEnergy);
        ConquestDailyPlanStep first = Assert.Single(result.Steps);
        Assert.Equal(10, first.RewardPointsEarned);
        Assert.True(first.RewardTargetReached);
        Assert.All(first.Team, unit => Assert.Contains(unit.DefinitionId, alpha));
        Assert.Equal(10, first.FeatProgress.Single().RewardPointsGranted);
    }

    [Fact]
    public async Task BuildAsync_StopsWhenEnergyCannotFundAnotherBattle()
    {
        DateTimeOffset now = new(2026, 9, 14, 5, 40, 0, TimeSpan.Zero);
        ConquestFeat feat = ConquestFeat.Create(
            Guid.NewGuid(),
            "Three Jedi wins",
            ConquestFeatScope.Global,
            null,
            15,
            3,
            0,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.Faction, "JEDI", [], 5));
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "conquest-energy",
            "Conquista",
            ConquestDifficulty.Hard,
            [feat],
            now);
        plan.ReplaceDailyGoal(
            availableEnergy: 40,
            energyCostPerBattle: 20,
            currentRewardPoints: 300,
            targetRewardPoints: 315,
            rewardTargetName: "Siguiente caja",
            now.AddMinutes(1));
        PlayerProfile profile = Profile(now, ["J1", "J2", "J3", "J4", "J5"]);
        GameDataCatalog catalog = Catalog(["J1", "J2", "J3", "J4", "J5"]);
        var service = CreateService(plan, profile, catalog);

        ConquestDailyPlanResult? result = await service.BuildAsync(
            123_456_789,
            new ConquestDailyPlanRequest(6),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result!.PlannedBattles);
        Assert.Equal("EnergyBudgetExhausted", result.StopReason);
        Assert.False(result.RewardTargetReached);
        Assert.Equal(40, result.EnergySpent);
        Assert.Equal(0, result.EnergyRemaining);
        Assert.Equal(300, result.ProjectedRewardPoints);
        Assert.Equal(0, result.ProjectedRewardPointsGained);
        Assert.Equal(0m, result.RewardPointsPerEnergy);
        Assert.Equal(2, result.Steps.Last().FeatProgress.Single().AfterProgress);
    }

    [Fact]
    public async Task BuildAsync_StopsBeforePlanningWhenTargetAlreadyReached()
    {
        DateTimeOffset now = new(2026, 9, 14, 5, 50, 0, TimeSpan.Zero);
        ConquestFeat feat = ConquestFeat.Create(
            Guid.NewGuid(),
            "Pending",
            ConquestFeatScope.Global,
            null,
            10,
            1,
            0,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.AnyCharacter, null, [], 1));
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "conquest-already-there",
            "Conquista",
            ConquestDifficulty.Hard,
            [feat],
            now);
        plan.ReplaceDailyGoal(100, 20, 530, 530, "Caja roja", now.AddMinutes(1));
        var service = CreateService(
            plan,
            Profile(now, ["J1", "J2", "J3", "J4", "J5"]),
            Catalog(["J1", "J2", "J3", "J4", "J5"]));

        ConquestDailyPlanResult? result = await service.BuildAsync(
            123_456_789,
            new ConquestDailyPlanRequest(6),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Empty(result!.Steps);
        Assert.Equal("RewardTargetReached", result.StopReason);
        Assert.True(result.RewardTargetReached);
        Assert.Equal(0, result.EnergySpent);
    }

    private static ConquestDailyPlanService CreateService(
        ConquestPlan plan,
        PlayerProfile profile,
        GameDataCatalog catalog) => new(
            new FakeRepository(plan),
            new FakePlayerProfileService(profile),
            new FakeCatalog(catalog));

    private static ConquestFeat SpecificFeat(
        string name,
        int points,
        int target,
        int progress,
        int expectedProgress,
        IReadOnlyCollection<string> units) => ConquestFeat.Create(
            Guid.NewGuid(),
            name,
            ConquestFeatScope.Global,
            null,
            points,
            target,
            progress,
            expectedProgress,
            ConquestFeatRule.Create(ConquestFeatRuleType.SpecificUnits, null, units, 5));

    private static PlayerProfile Profile(DateTimeOffset now, IReadOnlyCollection<string> ids) => PlayerProfile.Import(
        123_456_789,
        "player",
        "Player",
        null,
        null,
        85,
        10_000_000,
        now,
        [.. ids.Select((id, index) => Unit(id, 50_000 - index * 500))]);

    private static GameDataCatalog Catalog(IReadOnlyCollection<string> ids) => new(
        ids.ToDictionary(
            id => id,
            id => Definition(id, id),
            StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, GameSkillDefinition>(),
        []);

    private static RosterUnit Unit(string id, long gp) => new(
        id,
        id,
        85,
        7,
        13,
        7,
        6,
        gp,
        IsShip: false);

    private static GameUnitDefinition Definition(string id, string name) => new(
        id,
        false,
        null,
        name,
        null,
        ["JEDI"],
        []);

    private sealed class FakeRepository(ConquestPlan plan) : IConquestPlanRepository
    {
        public Task<ConquestPlan?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConquestPlan?>(id == plan.Id ? plan : null);

        public Task<ConquestPlan?> GetCurrentAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConquestPlan?>(allyCode == plan.AllyCode ? plan : null);

        public Task UpsertAsync(ConquestPlan value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakePlayerProfileService(PlayerProfile profile) : IPlayerProfileService
    {
        public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<PlayerProfile?>(allyCode == profile.AllyCode ? profile : null);

        public Task<PlayerProfile> SaveAsync(
            long allyCode,
            string name,
            long galacticPower,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlayerProfile> RefreshFromGameAsync(
            long allyCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCatalog(GameDataCatalog catalog) : ISwgohGameDataCatalog
    {
        public Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(catalog);
    }
}
