using Swgoh.Application.Abstractions;
using Swgoh.Application.Conquest;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Conquest;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Conquest;

public sealed class ConquestServiceTests
{
    [Fact]
    public async Task OptimizeCurrentAsync_PrefersTeamThatAdvancesMultipleFeats()
    {
        DateTimeOffset now = new(2026, 9, 13, 18, 0, 0, TimeSpan.Zero);
        ConquestFeat jediFeat = ConquestFeat.Create(
            Guid.NewGuid(),
            "Jedi wins",
            ConquestFeatScope.Sector,
            1,
            10,
            5,
            0,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.Faction, "JEDI", [], 3));
        ConquestFeat heroFeat = ConquestFeat.Create(
            Guid.NewGuid(),
            "Use HERO",
            ConquestFeatScope.Global,
            null,
            15,
            3,
            0,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.SpecificUnits, null, ["HERO"], 1));
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "conquest-1",
            "Conquista",
            ConquestDifficulty.Hard,
            [jediFeat, heroFeat],
            now);

        PlayerProfile profile = PlayerProfile.Import(
            123_456_789,
            "player",
            "Player",
            null,
            null,
            85,
            10_000_000,
            now,
            [
                Unit("HERO", 40_000),
                Unit("JEDI2", 35_000),
                Unit("JEDI3", 34_000),
                Unit("JEDI4", 33_000),
                Unit("JEDI5", 32_000),
                Unit("SITH1", 50_000),
                Unit("SITH2", 49_000)
            ]);
        GameDataCatalog catalog = new(
            new Dictionary<string, GameUnitDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["HERO"] = Definition("HERO", "Hero", "JEDI"),
                ["JEDI2"] = Definition("JEDI2", "Jedi 2", "JEDI"),
                ["JEDI3"] = Definition("JEDI3", "Jedi 3", "JEDI"),
                ["JEDI4"] = Definition("JEDI4", "Jedi 4", "JEDI"),
                ["JEDI5"] = Definition("JEDI5", "Jedi 5", "JEDI"),
                ["SITH1"] = Definition("SITH1", "Sith 1", "SITH"),
                ["SITH2"] = Definition("SITH2", "Sith 2", "SITH")
            },
            new Dictionary<string, GameSkillDefinition>(),
            []);
        var service = new ConquestService(
            new FakeRepository(plan),
            new FakePlayerProfileService(profile),
            new FakeCatalog(catalog),
            new FakeClock(now));

        ConquestOptimizationResult? result = await service.OptimizeCurrentAsync(
            123_456_789,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        ConquestTeamRecommendation best = Assert.IsType<ConquestTeamRecommendation>(result!.Recommendations.First());
        Assert.Contains(best.Team, unit => unit.DefinitionId == "HERO");
        Assert.True(best.Team.Count(unit => unit.Factions.Contains("JEDI")) >= 3);
        Assert.Equal(2, best.AdvancesFeats.Count);
        Assert.Empty(result.UncoveredFeatIds);
    }

    [Fact]
    public async Task OptimizeCurrentAsync_DoesNotRecommendCompletedFeat()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ConquestFeat completed = ConquestFeat.Create(
            Guid.NewGuid(),
            "Done",
            ConquestFeatScope.Global,
            null,
            20,
            1,
            1,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.AnyCharacter, null, [], 1));
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "event",
            "Conquista",
            ConquestDifficulty.Hard,
            [completed],
            now);
        PlayerProfile profile = PlayerProfile.Import(
            123_456_789,
            "p",
            "P",
            null,
            null,
            85,
            1,
            now,
            [Unit("A", 10_000)]);
        GameDataCatalog catalog = new(
            new Dictionary<string, GameUnitDefinition>
            {
                ["A"] = Definition("A", "A", "JEDI")
            },
            new Dictionary<string, GameSkillDefinition>(),
            []);
        var service = new ConquestService(
            new FakeRepository(plan),
            new FakePlayerProfileService(profile),
            new FakeCatalog(catalog),
            new FakeClock(now));

        ConquestOptimizationResult? result = await service.OptimizeCurrentAsync(
            123_456_789,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(0, result!.PendingFeats);
        Assert.Empty(result.Recommendations);
    }

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

    private static GameUnitDefinition Definition(string id, string name, params string[] factions) => new(
        id,
        false,
        null,
        name,
        null,
        factions,
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

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
