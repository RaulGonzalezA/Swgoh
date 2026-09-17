using Swgoh.Application.Abstractions;
using Swgoh.Application.Investments;

using Xunit;

namespace Swgoh.Application.UnitTests.Investments;

public sealed class InvestmentFarmingPlanServiceTests
{
    private const long AllyCode = 123_456_789L;
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 21, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_WhenTargetsShareInventory_DoesNotDoubleCountAvailableResources()
    {
        InvestmentTargetProgress[] targets =
        [
            CreateTarget("A", "Unit A", currentRelic: 5, targetRelic: 7),
            CreateTarget("B", "Unit B", currentRelic: 5, targetRelic: 7)
        ];
        PlayerInventorySnapshot inventory = CreateInventory(resource =>
            resource.Id == "credits" ? 1_000_000 : 1_000_000);
        var service = CreateService(targets, inventory);

        InvestmentFarmingPlan plan = await service.GetAsync(AllyCode, CancellationToken.None);

        FarmingResourcePriority credits = Assert.Single(plan.Resources, resource => resource.ResourceId == "credits");
        Assert.Equal(1_500_000, credits.Required);
        Assert.Equal(1_000_000, credits.Available);
        Assert.Equal(500_000, credits.Missing);
        Assert.Equal(2, credits.AffectedTargetCount);
        Assert.True(credits.SharedBottleneck);
        Assert.Equal(1, credits.Rank);
        Assert.Equal(1, plan.MissingResourceTypes);
        Assert.Equal(1, plan.SharedBottleneckCount);
    }

    [Fact]
    public async Task GetAsync_WithStarOnlyTarget_DoesNotInventMaterialRequirements()
    {
        InvestmentTargetProgress target = CreateTarget(
            "STAR",
            "Star Unit",
            currentRelic: 0,
            targetRelic: null,
            currentStars: 5,
            targetStars: 7);
        var service = CreateService([target], inventory: null);

        InvestmentFarmingPlan plan = await service.GetAsync(AllyCode, CancellationToken.None);

        Assert.Equal(1, plan.ActiveTargetCount);
        Assert.Equal(0, plan.RelicTargetCount);
        Assert.Equal(1, plan.StarOnlyTargetCount);
        Assert.Empty(plan.Resources);
        FarmingTargetPlan targetPlan = Assert.Single(plan.Targets);
        Assert.False(targetPlan.RelicMaterialsTracked);
        Assert.Contains("fragmentos", targetPlan.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_WithoutInventory_KeepsRequirementsButLeavesDeficitUnknown()
    {
        InvestmentTargetProgress target = CreateTarget("A", "Unit A", currentRelic: 5, targetRelic: 7);
        var service = CreateService([target], inventory: null);

        InvestmentFarmingPlan plan = await service.GetAsync(AllyCode, CancellationToken.None);

        Assert.False(plan.HasInventorySnapshot);
        FarmingResourcePriority credits = Assert.Single(plan.Resources, resource => resource.ResourceId == "credits");
        Assert.Equal(750_000, credits.Required);
        Assert.Null(credits.Available);
        Assert.Null(credits.Missing);
        Assert.Null(credits.Coverage);
    }

    private static InvestmentFarmingPlanService CreateService(
        IReadOnlyCollection<InvestmentTargetProgress> targets,
        PlayerInventorySnapshot? inventory) => new(
            new FakeTargetService(targets),
            new FakeInventoryService(inventory),
            new FixedClock(Now));

    private static InvestmentTargetProgress CreateTarget(
        string definitionId,
        string name,
        int currentRelic,
        int? targetRelic,
        int currentStars = 7,
        int? targetStars = null) => new(
            AllyCode,
            definitionId,
            name,
            null,
            false,
            currentRelic,
            currentStars,
            targetRelic,
            targetStars,
            false,
            0.5m,
            targetRelic is int relic ? Math.Max(0, relic - currentRelic) : 0,
            targetStars is int stars ? Math.Max(0, stars - currentStars) : 0,
            "test",
            null,
            Now.AddDays(-1),
            Now);

    private static PlayerInventorySnapshot CreateInventory(Func<InventoryResourceDefinition, long> quantity) => new(
        AllyCode,
        Now,
        "test",
        [
            .. PlayerInventoryCatalog.Resources.Select(resource => new PlayerInventoryResource(
                resource.Id,
                resource.Name,
                quantity(resource)))
        ]);

    private sealed class FakeTargetService(IReadOnlyCollection<InvestmentTargetProgress> targets)
        : IInvestmentTargetService
    {
        public Task<IReadOnlyCollection<InvestmentTargetProgress>> GetAllAsync(
            long allyCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(allyCode == AllyCode ? targets : (IReadOnlyCollection<InvestmentTargetProgress>)[]);

        public Task<InvestmentTargetProgress?> GetAsync(
            long allyCode,
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(targets.FirstOrDefault(target =>
                allyCode == AllyCode
                && string.Equals(target.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase)));

        public Task<InvestmentTargetProgress> SaveAsync(
            long allyCode,
            string definitionId,
            InvestmentTargetUpdate update,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            long allyCode,
            string definitionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeInventoryService(PlayerInventorySnapshot? inventory) : IPlayerInventoryService
    {
        public Task<PlayerInventorySnapshot?> GetAsync(
            long allyCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(inventory?.AllyCode == allyCode ? inventory : null);

        public Task<PlayerInventorySnapshot> ImportAsync(
            long allyCode,
            PlayerInventoryImport inventoryImport,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
