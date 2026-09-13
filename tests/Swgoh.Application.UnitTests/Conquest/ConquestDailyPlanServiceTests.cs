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
        var service = new ConquestDailyPlanService(
            new FakeRepository(plan),
            new FakePlayerProfileService(profile),
            new FakeCatalog(catalog));

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
    }

    [Fact]
    public async Task BuildAsync_RotatesTeamWhenNextFeatRequiresDifferentUnits()
    {
        DateTimeOffset now = new(2026, 9, 14, 0, 20, 0, TimeSpan.Zero);
        string[] alpha = ["A1", "A2", "A3", "A4", "A5"];
        string[] beta = ["B1", "B2", "B3", "B4", "B5"];
        ConquestFeat alphaFeat = ConquestFeat.Create(
            Guid.NewGuid(),
            "Alpha win",
            ConquestFeatScope.Global,
            null,
            20,
            1,
            0,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.SpecificUnits, null, alpha, 5));
        ConquestFeat betaFeat = ConquestFeat.Create(
            Guid.NewGuid(),
            "Beta win",
            ConquestFeatScope.Global,
            null,
            10,
            1,
            0,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.SpecificUnits, null, beta, 5));
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "conquest-rotate",
            "Conquista",
            ConquestDifficulty.Hard,
            [alphaFeat, betaFeat],
            now);
        PlayerProfile profile = Profile(now, [.. alpha, .. beta]);
        GameDataCatalog catalog = Catalog([.. alpha, .. beta]);
        var service = new ConquestDailyPlanService(
            new FakeRepository(plan),
            new FakePlayerProfileService(profile),
            new FakeCatalog(catalog));

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
