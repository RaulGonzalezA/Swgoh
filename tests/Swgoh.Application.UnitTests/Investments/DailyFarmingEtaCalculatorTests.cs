using Swgoh.Application.Investments;

using Xunit;

namespace Swgoh.Application.UnitTests.Investments;

public sealed class DailyFarmingEtaCalculatorTests
{
    private const long AllyCode = 123_456_789L;
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithFragmentedOnlyAndNoRefreshes_EstimatesThreeDays()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            missing: 40,
            rank: 1);
        InvestmentFarmingPlan plan = Plan(
            [fragmented],
            [Target("UNIT_A", "Unit A")]);

        DailyFarmingEtaProjection result = DailyFarmingEtaCalculator.Build(
            plan,
            Budget(),
            Baselines(),
            Now);

        DailyResourceEta resource = Assert.Single(result.ResourceEtas);
        Assert.Equal(1.35m, resource.ExpectedDropsPerAttempt);
        Assert.Equal(165, resource.PlannedDailyEnergy);
        Assert.Equal(3, resource.EstimatedDays);
        Assert.Equal(Now.AddDays(3), resource.EstimatedCompletionAtUtc);

        DailyTargetEta target = Assert.Single(result.TargetEtas);
        Assert.True(target.FullEstimateAvailable);
        Assert.False(target.ReadyNow);
        Assert.Equal(3, target.EstimatedDays);
        Assert.Equal(Now.AddDays(3), target.EstimatedCompletionAtUtc);
        Assert.Equal(0, target.UnknownBlockingResourceTypes);
    }

    [Fact]
    public void Build_WithCantinaRefresh_UsesPlannedEnergyAndShortensEta()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            missing: 40,
            rank: 1);
        InvestmentFarmingPlan plan = Plan(
            [fragmented],
            [Target("UNIT_A", "Unit A")]);

        DailyFarmingEtaProjection result = DailyFarmingEtaCalculator.Build(
            plan,
            Budget(cantinaEnergyGained: 120),
            Baselines(),
            Now);

        DailyResourceEta resource = Assert.Single(result.ResourceEtas);
        Assert.Equal(285, resource.PlannedDailyEnergy);
        Assert.Equal(2, resource.EstimatedDays);
        Assert.Equal(Now.AddDays(2), resource.EstimatedCompletionAtUtc);
    }

    [Fact]
    public void Build_WithUnknownScavengerBlocker_ReturnsPartialEta()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            missing: 40,
            rank: 1);
        FarmingResourcePriority carbonite = Resource(
            "carbonite_circuit_board",
            "Carbonite Circuit Board",
            missing: 120,
            rank: 2,
            kind: InventoryResourceKind.RelicMaterial,
            lane: FarmingLane.Scavenger);
        InvestmentFarmingPlan plan = Plan(
            [fragmented, carbonite],
            [Target("UNIT_A", "Unit A", blockingResourceTypes: 2)]);

        DailyFarmingEtaProjection result = DailyFarmingEtaCalculator.Build(
            plan,
            Budget(),
            Baselines(),
            Now);

        DailyTargetEta target = Assert.Single(result.TargetEtas);
        Assert.False(target.FullEstimateAvailable);
        Assert.False(target.ReadyNow);
        Assert.Null(target.EstimatedDays);
        Assert.Null(target.EstimatedCompletionAtUtc);
        Assert.Equal(1, target.ModeledBlockingResourceTypes);
        Assert.Equal(1, target.UnknownBlockingResourceTypes);
        Assert.Equal(Now.AddDays(3), target.KnownBottleneckCompletionAtUtc);
        Assert.Contains("ETA parcial", target.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithAllTrackedMaterialsCovered_MarksTargetReadyNow()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            missing: 0,
            rank: 1);
        InvestmentFarmingPlan plan = Plan(
            [fragmented],
            [Target("UNIT_A", "Unit A", blockingResourceTypes: 0)]);

        DailyFarmingEtaProjection result = DailyFarmingEtaCalculator.Build(
            plan,
            Budget(),
            Baselines(),
            Now);

        Assert.Empty(result.ResourceEtas);
        DailyTargetEta target = Assert.Single(result.TargetEtas);
        Assert.True(target.ReadyNow);
        Assert.True(target.FullEstimateAvailable);
        Assert.Equal(0, target.EstimatedDays);
        Assert.Equal(Now, target.EstimatedCompletionAtUtc);
    }

    [Fact]
    public void Build_WithStarSteps_KeepsFullEtaUnavailable()
    {
        InvestmentFarmingPlan plan = Plan(
            [],
            [Target(
                "UNIT_A",
                "Unit A",
                blockingResourceTypes: 0,
                relicMaterialsTracked: false,
                starStepsRemaining: 1)]);

        DailyFarmingEtaProjection result = DailyFarmingEtaCalculator.Build(
            plan,
            Budget(),
            Baselines(),
            Now);

        DailyTargetEta target = Assert.Single(result.TargetEtas);
        Assert.False(target.FullEstimateAvailable);
        Assert.False(target.ReadyNow);
        Assert.Equal(1, target.UnknownBlockingResourceTypes);
        Assert.Contains("fragmentos", target.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithoutInventory_DoesNotInventEta()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            missing: 40,
            rank: 1);
        InvestmentFarmingPlan plan = Plan(
            [fragmented],
            [Target("UNIT_A", "Unit A")],
            hasInventory: false);

        DailyFarmingEtaProjection result = DailyFarmingEtaCalculator.Build(
            plan,
            Budget(),
            Baselines(),
            Now);

        Assert.Empty(result.ResourceEtas);
        DailyTargetEta target = Assert.Single(result.TargetEtas);
        Assert.False(target.FullEstimateAvailable);
        Assert.Contains("inventario", target.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithMultipleSignalResources_UsesConservativePortfolioOrder()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            missing: 40,
            rank: 1);
        FarmingResourcePriority flawed = Resource(
            "signal_data_flawed",
            "Flawed Signal Data",
            missing: 65,
            rank: 2);
        InvestmentFarmingPlan plan = Plan(
            [fragmented, flawed],
            [Target("UNIT_A", "Unit A", blockingResourceTypes: 2)]);

        DailyFarmingEtaProjection result = DailyFarmingEtaCalculator.Build(
            plan,
            Budget(),
            Baselines(),
            Now);

        DailyResourceEta fragmentedEta = Assert.Single(
            result.ResourceEtas,
            eta => eta.ResourceId == "signal_data_fragmented");
        DailyResourceEta flawedEta = Assert.Single(
            result.ResourceEtas,
            eta => eta.ResourceId == "signal_data_flawed");

        Assert.Equal(3, fragmentedEta.EstimatedDays);
        Assert.Equal(13, flawedEta.EstimatedDays);
        DailyTargetEta target = Assert.Single(result.TargetEtas);
        Assert.Equal(13, target.EstimatedDays);
    }

    private static InvestmentFarmingPlan Plan(
        IReadOnlyCollection<FarmingResourcePriority> resources,
        IReadOnlyCollection<FarmingTargetPlan> targets,
        bool hasInventory = true) => new(
            AllyCode,
            Now,
            hasInventory ? Now.AddMinutes(-20) : null,
            hasInventory ? "test" : null,
            targets.Count,
            targets.Count(target => target.RelicMaterialsTracked),
            targets.Count(target => !target.RelicMaterialsTracked && target.StarStepsRemaining > 0),
            resources.Count(resource => resource.Missing is > 0),
            resources.Count(resource => resource.SharedBottleneck),
            resources,
            targets);

    private static FarmingResourcePriority Resource(
        string id,
        string name,
        long missing,
        int rank,
        InventoryResourceKind kind = InventoryResourceKind.SignalData,
        FarmingLane lane = FarmingLane.SignalData) => new(
            rank,
            id,
            name,
            kind,
            lane,
            lane.ToString(),
            "test",
            missing + 10,
            10,
            missing,
            missing == 0 ? 1m : 0.1m,
            1,
            false,
            missing == 0 ? "Cubierto" : "Alta",
            [new FarmingTargetDependency("UNIT_A", "Unit A", missing + 10)]);

    private static FarmingTargetPlan Target(
        string id,
        string name,
        int blockingResourceTypes = 1,
        bool relicMaterialsTracked = true,
        int starStepsRemaining = 0) => new(
            id,
            name,
            null,
            5,
            relicMaterialsTracked ? 7 : null,
            7 - starStepsRemaining,
            starStepsRemaining > 0 ? 7 : null,
            relicMaterialsTracked ? 2 : 0,
            starStepsRemaining,
            blockingResourceTypes == 0 ? 1m : 0.2m,
            blockingResourceTypes,
            0,
            relicMaterialsTracked,
            "test");

    private static IReadOnlyCollection<DailyEnergyBaseline> Baselines() =>
    [
        new(
            DailyFarmingChannel.CantinaEnergy,
            "Cantina",
            120,
            45,
            165,
            "test")
    ];

    private static DailyCrystalBudgetPlan Budget(int cantinaEnergyGained = 0)
    {
        IReadOnlyCollection<DailyRefreshRecommendation> refreshes = cantinaEnergyGained == 0
            ? []
            :
            [
                new DailyRefreshRecommendation(
                    DailyFarmingChannel.CantinaEnergy,
                    "Energía de Cantina",
                    1,
                    100,
                    cantinaEnergyGained,
                    165,
                    165 + cantinaEnergyGained,
                    100,
                    "test")
            ];
        int spent = cantinaEnergyGained == 0 ? 0 : 100;
        return new DailyCrystalBudgetPlan(
            spent,
            spent,
            0,
            spent == 0 ? "F2P" : "test",
            refreshes);
    }
}
