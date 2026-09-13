using Swgoh.Application.Abstractions;
using Swgoh.Application.Conquest;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Conquest;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Conquest;

public sealed class ConquestDataDiskOptimizationTests
{
    [Fact]
    public async Task OptimizeCurrentAsync_SelectsLoadoutWithTeamAndFeatSynergy()
    {
        DateTimeOffset now = new(2026, 9, 13, 22, 15, 0, TimeSpan.Zero);
        Guid featId = Guid.NewGuid();
        ConquestFeat feat = ConquestFeat.Create(
            featId,
            "Jedi wins",
            ConquestFeatScope.Sector,
            1,
            10,
            5,
            0,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.Faction, "JEDI", [], 3));
        ConquestPlan plan = ConquestPlan.Create(
            123_456_789,
            "conquest-disks",
            "Conquista",
            ConquestDifficulty.Hard,
            [feat],
            now);

        ConquestDataDisk genericDisk = ConquestDataDisk.Create(
            Guid.NewGuid(),
            "Generic",
            2,
            1m,
            ConquestDataDiskTarget.Create(ConquestDataDiskTargetType.AnyTeam, null, [], 1),
            [],
            null);
        ConquestDataDisk jediDisk = ConquestDataDisk.Create(
            Guid.NewGuid(),
            "Jedi feat support",
            3,
            5m,
            ConquestDataDiskTarget.Create(ConquestDataDiskTargetType.Faction, "JEDI", [], 3),
            [featId],
            null);
        ConquestDiskLoadout genericLoadout = ConquestDiskLoadout.Create(
            Guid.NewGuid(),
            "General",
            [genericDisk.Id]);
        ConquestDiskLoadout jediLoadout = ConquestDiskLoadout.Create(
            Guid.NewGuid(),
            "Jedi farm",
            [jediDisk.Id]);
        plan.ReplaceDataDisks(
            12,
            [genericDisk, jediDisk],
            [genericLoadout, jediLoadout],
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
                Unit("JEDI1", 40_000),
                Unit("JEDI2", 39_000),
                Unit("JEDI3", 38_000),
                Unit("JEDI4", 37_000),
                Unit("JEDI5", 36_000),
                Unit("SITH1", 50_000)
            ]);
        GameDataCatalog catalog = new(
            new Dictionary<string, GameUnitDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["JEDI1"] = Definition("JEDI1", "Jedi 1", "JEDI"),
                ["JEDI2"] = Definition("JEDI2", "Jedi 2", "JEDI"),
                ["JEDI3"] = Definition("JEDI3", "Jedi 3", "JEDI"),
                ["JEDI4"] = Definition("JEDI4", "Jedi 4", "JEDI"),
                ["JEDI5"] = Definition("JEDI5", "Jedi 5", "JEDI"),
                ["SITH1"] = Definition("SITH1", "Sith 1", "SITH")
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
        ConquestTeamRecommendation recommendation = Assert.IsType<ConquestTeamRecommendation>(
            result!.Recommendations.First());
        Assert.NotNull(recommendation.DiskLoadout);
        Assert.Equal("Jedi farm", recommendation.DiskLoadout!.LoadoutName);
        Assert.Contains(featId, recommendation.DiskLoadout.MatchedFeatIds);
        Assert.Equal(6.5m, recommendation.DiskLoadout.PlannerBonus);
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
