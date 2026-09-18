using Swgoh.Application.Abstractions;
using Swgoh.Application.Investments;

using Xunit;

namespace Swgoh.Application.UnitTests.Investments;

public sealed class InvestmentDailyFarmingPlanServiceTests
{
    private const long AllyCode = 123_456_789L;
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 5, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_WithFragmentedSignalData_UsesExactCantinaNode()
    {
        InvestmentFarmingPlan farmingPlan = CreatePlan(
            [CreateResource(
                "signal_data_fragmented",
                "Fragmented Signal Data",
                InventoryResourceKind.SignalData,
                FarmingLane.SignalData,
                missing: 40,
                affectedTargets: 2,
                shared: true,
                priority: "Crítica")]);
        var service = CreateService(farmingPlan);

        InvestmentDailyFarmingPlan result = await service.GetAsync(AllyCode, CancellationToken.None);

        DailyFarmingAction action = Assert.Single(
            result.Actions,
            item => item.ResourceId == "signal_data_fragmented");
        Assert.Equal(DailyFarmingChannel.CantinaEnergy, action.Channel);
        Assert.Equal(DailyFarmingPrecision.Exact, action.Precision);
        Assert.Equal("Cantina 8-C", action.Source);
        Assert.Equal(16, action.EnergyCostPerAttempt);
        Assert.Equal(165, action.BaselineFreeEnergy);
        Assert.True(action.SharedBottleneck);
        Assert.Equal(1, action.Rank);
    }

    [Fact]
    public async Task GetAsync_WithTwoSignalDataDeficits_UsesMatchingSector9DualNode()
    {
        InvestmentFarmingPlan farmingPlan = CreatePlan(
        [
            CreateResource(
                "signal_data_fragmented",
                "Fragmented Signal Data",
                InventoryResourceKind.SignalData,
                FarmingLane.SignalData,
                missing: 40,
                affectedTargets: 2,
                shared: true,
                priority: "Crítica"),
            CreateResource(
                "signal_data_incomplete",
                "Incomplete Signal Data",
                InventoryResourceKind.SignalData,
                FarmingLane.SignalData,
                missing: 25,
                affectedTargets: 1,
                priority: "Alta")
        ]);
        var service = CreateService(farmingPlan);

        InvestmentDailyFarmingPlan result = await service.GetAsync(AllyCode, 100, CancellationToken.None);

        DailyFarmingAction action = Assert.Single(
            result.Actions,
            item => item.Channel == DailyFarmingChannel.CantinaEnergy);
        Assert.Equal("Cantina 9-B", action.Source);
        Assert.Equal(20, action.EnergyCostPerAttempt);
        Assert.Equal("Nodo dual exacto", action.PrecisionLabel);
        Assert.DoesNotContain(
            result.Actions,
            item => item.Source is "Cantina 8-C" or "Cantina 8-F");
        DailyRefreshRecommendation refresh = Assert.Single(result.CrystalBudget.Refreshes);
        Assert.Equal(DailyFarmingChannel.CantinaEnergy, refresh.Channel);
        Assert.Equal(100, refresh.CrystalCost);
    }

    [Fact]
    public async Task GetAsync_WithScavengerFeedstock_UsesGuidedNormalEnergyNodes()
    {
        InvestmentFarmingPlan farmingPlan = CreatePlan(
        [
            CreateResource(
                "carbonite_circuit_board",
                "Carbonite Circuit Board",
                InventoryResourceKind.RelicMaterial,
                FarmingLane.Scavenger,
                missing: 120,
                priority: "Alta"),
            CreateResource(
                "bronzium_wiring",
                "Bronzium Wiring",
                InventoryResourceKind.RelicMaterial,
                FarmingLane.Scavenger,
                missing: 80,
                priority: "Alta")
        ]);
        var service = CreateService(farmingPlan);

        InvestmentDailyFarmingPlan result = await service.GetAsync(AllyCode, CancellationToken.None);

        DailyFarmingAction carbonite = Assert.Single(
            result.Actions,
            item => item.ResourceId == "carbonite_circuit_board");
        DailyFarmingAction bronzium = Assert.Single(
            result.Actions,
            item => item.ResourceId == "bronzium_wiring");
        Assert.Equal(DailyFarmingChannel.NormalEnergy, carbonite.Channel);
        Assert.Equal(DailyFarmingPrecision.Guided, carbonite.Precision);
        Assert.Equal("Light Side 1-C (Normal)", carbonite.Source);
        Assert.Equal(6, carbonite.EnergyCostPerAttempt);
        Assert.Equal("Light Side 7-B (Normal)", bronzium.Source);
        Assert.Equal(10, bronzium.EnergyCostPerAttempt);
    }

    [Fact]
    public async Task GetAsync_WithActiveTargets_AddsFleetGuardrailInsteadOfInventingNode()
    {
        InvestmentFarmingPlan farmingPlan = CreatePlan([]);
        var service = CreateService(farmingPlan);

        InvestmentDailyFarmingPlan result = await service.GetAsync(AllyCode, 300, CancellationToken.None);

        DailyFarmingAction action = Assert.Single(result.Actions);
        Assert.Equal(DailyFarmingChannel.FleetEnergy, action.Channel);
        Assert.Equal(DailyFarmingPrecision.Check, action.Precision);
        Assert.Null(action.ResourceId);
        Assert.Null(action.EnergyCostPerAttempt);
        Assert.Equal(285, action.BaselineFreeEnergy);
        Assert.Contains("no conoce un nodo Fleet exacto", action.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.CrystalBudget.Refreshes);
        Assert.Equal(0, result.CrystalBudget.CrystalsSpent);
        Assert.Equal(300, result.CrystalBudget.CrystalsUnspent);
    }

    [Fact]
    public async Task GetAsync_ExposesFreeEnergyBaselinesWithoutCrystalRefreshes()
    {
        InvestmentFarmingPlan farmingPlan = CreatePlan([]);
        var service = CreateService(farmingPlan);

        InvestmentDailyFarmingPlan result = await service.GetAsync(AllyCode, CancellationToken.None);

        DailyEnergyBaseline cantina = Assert.Single(
            result.EnergyBaselines,
            item => item.Channel == DailyFarmingChannel.CantinaEnergy);
        DailyEnergyBaseline normal = Assert.Single(
            result.EnergyBaselines,
            item => item.Channel == DailyFarmingChannel.NormalEnergy);
        DailyEnergyBaseline fleet = Assert.Single(
            result.EnergyBaselines,
            item => item.Channel == DailyFarmingChannel.FleetEnergy);
        Assert.Equal(165, cantina.BaselineFreeEnergy);
        Assert.Equal(375, normal.BaselineFreeEnergy);
        Assert.Equal(285, fleet.BaselineFreeEnergy);
        Assert.Equal("F2P", result.CrystalBudget.ProfileLabel);
        Assert.Equal(0, result.CrystalBudget.DailyCrystalBudget);
        Assert.Empty(result.CrystalBudget.Refreshes);
    }

    [Fact]
    public async Task GetAsync_With150CrystalBudget_SplitsCriticalCantinaAndNormalRefreshes()
    {
        InvestmentFarmingPlan farmingPlan = CreatePlan(
        [
            CreateResource(
                "signal_data_fragmented",
                "Fragmented Signal Data",
                InventoryResourceKind.SignalData,
                FarmingLane.SignalData,
                missing: 40,
                affectedTargets: 2,
                shared: true,
                priority: "Crítica"),
            CreateResource(
                "carbonite_circuit_board",
                "Carbonite Circuit Board",
                InventoryResourceKind.RelicMaterial,
                FarmingLane.Scavenger,
                missing: 120,
                priority: "Alta")
        ]);
        var service = CreateService(farmingPlan);

        InvestmentDailyFarmingPlan result = await service.GetAsync(AllyCode, 150, CancellationToken.None);

        Assert.Equal(150, result.CrystalBudget.CrystalsSpent);
        Assert.Equal(0, result.CrystalBudget.CrystalsUnspent);
        Assert.Equal(2, result.CrystalBudget.RefreshCount);
        DailyRefreshRecommendation cantina = Assert.Single(
            result.CrystalBudget.Refreshes,
            refresh => refresh.Channel == DailyFarmingChannel.CantinaEnergy);
        DailyRefreshRecommendation normal = Assert.Single(
            result.CrystalBudget.Refreshes,
            refresh => refresh.Channel == DailyFarmingChannel.NormalEnergy);
        Assert.Equal(1, cantina.RefreshCount);
        Assert.Equal(100, cantina.CrystalCost);
        Assert.Equal(285, cantina.PlannedDailyEnergy);
        Assert.Equal(1, normal.RefreshCount);
        Assert.Equal(50, normal.CrystalCost);
        Assert.Equal(495, normal.PlannedDailyEnergy);
    }

    [Fact]
    public async Task GetAsync_WithSignalDataTarget_ExposesBudgetAwareEta()
    {
        InvestmentFarmingPlan farmingPlan = CreatePlan(
            [CreateResource(
                "signal_data_fragmented",
                "Fragmented Signal Data",
                InventoryResourceKind.SignalData,
                FarmingLane.SignalData,
                missing: 40,
                priority: "Crítica")],
            targets: [CreateTarget()]);
        var service = CreateService(farmingPlan);

        InvestmentDailyFarmingPlan result = await service.GetAsync(AllyCode, 100, CancellationToken.None);

        DailyResourceEta resourceEta = Assert.Single(result.ResourceEtas);
        Assert.Equal(285, resourceEta.PlannedDailyEnergy);
        Assert.Equal(2, resourceEta.EstimatedDays);
        DailyTargetEta targetEta = Assert.Single(result.TargetEtas);
        Assert.True(targetEta.FullEstimateAvailable);
        Assert.Equal(2, targetEta.EstimatedDays);
        Assert.Equal(Now.AddDays(2), targetEta.EstimatedCompletionAtUtc);
        Assert.Equal(1, result.FullyEstimatedTargetCount);
    }

    [Fact]
    public async Task GetAsync_ExposesBudgetScenariosAndMarksCurrentBudget()
    {
        InvestmentFarmingPlan farmingPlan = CreatePlan(
            [CreateResource(
                "signal_data_fragmented",
                "Fragmented Signal Data",
                InventoryResourceKind.SignalData,
                FarmingLane.SignalData,
                missing: 100,
                priority: "Crítica")],
            targets: [CreateTarget()]);
        var service = CreateService(farmingPlan);

        InvestmentDailyFarmingPlan result = await service.GetAsync(AllyCode, 100, CancellationToken.None);

        Assert.Equal([0, 50, 100, 150, 300], result.BudgetScenarios.Select(item => item.DailyCrystalBudget));
        DailyBudgetScenario current = Assert.Single(result.BudgetScenarios, scenario => scenario.IsCurrent);
        Assert.Equal(100, current.DailyCrystalBudget);
        Assert.Equal(100, current.CrystalsSpent);
        Assert.Equal(5, current.ModeledPortfolioDays);
        Assert.Equal(3, current.DaysSavedVsF2P);
    }

    [Fact]
    public async Task GetAsync_With50CrystalBudgetAndCantinaOnly_LeavesCrystalsUnspent()
    {
        InvestmentFarmingPlan farmingPlan = CreatePlan(
            [CreateResource(
                "signal_data_fragmented",
                "Fragmented Signal Data",
                InventoryResourceKind.SignalData,
                FarmingLane.SignalData,
                missing: 40,
                priority: "Crítica")]);
        var service = CreateService(farmingPlan);

        InvestmentDailyFarmingPlan result = await service.GetAsync(AllyCode, 50, CancellationToken.None);

        Assert.Equal(0, result.CrystalBudget.CrystalsSpent);
        Assert.Equal(50, result.CrystalBudget.CrystalsUnspent);
        Assert.Empty(result.CrystalBudget.Refreshes);
    }

    private static InvestmentDailyFarmingPlanService CreateService(InvestmentFarmingPlan plan) => new(
        new FakeFarmingPlanService(plan),
        new FixedClock(Now));

    private static InvestmentFarmingPlan CreatePlan(
        IReadOnlyCollection<FarmingResourcePriority> resources,
        int activeTargets = 1,
        IReadOnlyCollection<FarmingTargetPlan>? targets = null) => new(
            AllyCode,
            Now,
            Now.AddMinutes(-30),
            "test",
            activeTargets,
            activeTargets,
            0,
            resources.Count(resource => resource.Missing is > 0),
            resources.Count(resource => resource.SharedBottleneck),
            resources,
            targets ?? []);

    private static FarmingTargetPlan CreateTarget() => new(
        "UNIT",
        "Unit",
        null,
        5,
        7,
        7,
        null,
        2,
        0,
        0.2m,
        1,
        0,
        true,
        "test");

    private static FarmingResourcePriority CreateResource(
        string resourceId,
        string resourceName,
        InventoryResourceKind kind,
        FarmingLane lane,
        long missing,
        int affectedTargets = 1,
        bool shared = false,
        string priority = "Alta") => new(
            1,
            resourceId,
            resourceName,
            kind,
            lane,
            lane.ToString(),
            "test",
            missing + 10,
            10,
            missing,
            0.1m,
            affectedTargets,
            shared,
            priority,
            [new FarmingTargetDependency("UNIT", "Unit", missing + 10)]);

    private sealed class FakeFarmingPlanService(InvestmentFarmingPlan plan) : IInvestmentFarmingPlanService
    {
        public Task<InvestmentFarmingPlan> GetAsync(
            long allyCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(plan with { AllyCode = allyCode });
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
