using Swgoh.Application.Investments;

using Xunit;

namespace Swgoh.Application.UnitTests.Investments;

public sealed class DailyFarmingBudgetScenarioCalculatorTests
{
    private const long AllyCode = 123_456_789L;
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithCustomCurrentBudget_IncludesPresetsAndCurrentOnce()
    {
        InvestmentFarmingPlan plan = Plan(
            [SignalResource(missing: 100)],
            [Target()]);
        DailyFarmingAction[] actions = [SignalAction()];

        IReadOnlyCollection<DailyBudgetScenario> result =
            DailyFarmingBudgetScenarioCalculator.Build(
                plan,
                actions,
                Baselines(),
                currentBudget: 100,
                Now);

        Assert.Equal([0, 50, 100, 150, 300], result.Select(item => item.DailyCrystalBudget));
        DailyBudgetScenario current = Assert.Single(result, scenario => scenario.IsCurrent);
        Assert.Equal(100, current.DailyCrystalBudget);
        Assert.Equal("Personalizado · 100", current.ProfileLabel);
    }

    [Fact]
    public void Build_WithCantinaSignalFarm_ShowsTimeSavedVsF2P()
    {
        InvestmentFarmingPlan plan = Plan(
            [SignalResource(missing: 100)],
            [Target()]);
        DailyFarmingAction[] actions = [SignalAction()];

        IReadOnlyCollection<DailyBudgetScenario> result =
            DailyFarmingBudgetScenarioCalculator.Build(
                plan,
                actions,
                Baselines(),
                currentBudget: 150,
                Now);

        DailyBudgetScenario f2p = Assert.Single(
            result,
            scenario => scenario.DailyCrystalBudget == 0);
        DailyBudgetScenario efficient = Assert.Single(
            result,
            scenario => scenario.DailyCrystalBudget == 150);
        DailyBudgetScenario accelerated = Assert.Single(
            result,
            scenario => scenario.DailyCrystalBudget == 300);

        Assert.Equal(8, f2p.ModeledPortfolioDays);
        Assert.Equal(0, f2p.DaysSavedVsF2P);
        Assert.Equal(0, f2p.CrystalsSpent);

        Assert.Equal(5, efficient.ModeledPortfolioDays);
        Assert.Equal(3, efficient.DaysSavedVsF2P);
        Assert.Equal(100, efficient.CrystalsSpent);
        Assert.Equal(50, efficient.CrystalsUnspent);
        Assert.Equal(1, efficient.RefreshCount);
        Assert.Equal(120, efficient.EnergyGained);

        Assert.Equal(3, accelerated.ModeledPortfolioDays);
        Assert.Equal(5, accelerated.DaysSavedVsF2P);
        Assert.Equal(300, accelerated.CrystalsSpent);
        Assert.Equal(3, accelerated.RefreshCount);
        Assert.Equal(360, accelerated.EnergyGained);
        Assert.Equal(Now.AddDays(3), accelerated.ModeledPortfolioCompletionAtUtc);
    }

    [Fact]
    public void Build_WithBudgetBelowCantinaRefreshCost_KeepsSameEtaAndReservesCrystals()
    {
        InvestmentFarmingPlan plan = Plan(
            [SignalResource(missing: 100)],
            [Target()]);
        DailyFarmingAction[] actions = [SignalAction()];

        IReadOnlyCollection<DailyBudgetScenario> result =
            DailyFarmingBudgetScenarioCalculator.Build(
                plan,
                actions,
                Baselines(),
                currentBudget: 50,
                Now);

        DailyBudgetScenario f2p = Assert.Single(
            result,
            scenario => scenario.DailyCrystalBudget == 0);
        DailyBudgetScenario saving = Assert.Single(
            result,
            scenario => scenario.DailyCrystalBudget == 50);

        Assert.Equal(f2p.ModeledPortfolioDays, saving.ModeledPortfolioDays);
        Assert.Equal(0, saving.CrystalsSpent);
        Assert.Equal(50, saving.CrystalsUnspent);
        Assert.Equal(0, saving.RefreshCount);
        Assert.Equal(0, saving.DaysSavedVsF2P);
    }

    [Fact]
    public void Build_TargetImpactsExposePerTargetSavings()
    {
        InvestmentFarmingPlan plan = Plan(
            [SignalResource(missing: 100)],
            [Target()]);
        DailyFarmingAction[] actions = [SignalAction()];

        IReadOnlyCollection<DailyBudgetScenario> result =
            DailyFarmingBudgetScenarioCalculator.Build(
                plan,
                actions,
                Baselines(),
                currentBudget: 300,
                Now);

        DailyBudgetScenario accelerated = Assert.Single(
            result,
            scenario => scenario.DailyCrystalBudget == 300);
        DailyBudgetScenarioTarget target = Assert.Single(accelerated.Targets);

        Assert.Equal("UNIT_A", target.DefinitionId);
        Assert.True(target.FullEstimateAvailable);
        Assert.Equal(3, target.EstimatedDays);
        Assert.Equal(5, target.DaysSavedVsF2P);
    }

    [Fact]
    public void Build_WithCarboniteFeedstock_ShowsNormalEnergyRefreshSavings()
    {
        FarmingResourcePriority carbonite = new(
            1,
            "carbonite_circuit_board",
            "Carbonite Circuit Board",
            InventoryResourceKind.RelicMaterial,
            FarmingLane.Scavenger,
            "Chatarrero",
            "test",
            150,
            10,
            140,
            0.067m,
            1,
            false,
            "Crítica",
            [new FarmingTargetDependency("UNIT_A", "Unit A", 150)]);
        InvestmentFarmingPlan plan = Plan(
            [carbonite],
            [Target()]);
        DailyFarmingAction[] actions =
        [
            new DailyFarmingAction(
                1,
                DailyFarmingChannel.NormalEnergy,
                "Energía normal",
                DailyFarmingPrecision.Guided,
                "Feedstock sugerido",
                "Alimenta Carbonite Circuit Board",
                "test",
                carbonite.ResourceId,
                carbonite.ResourceName,
                "Light Side 1-C (Normal)",
                6,
                375,
                140,
                1,
                false,
                "Crítica",
                "test")
        ];

        IReadOnlyCollection<DailyBudgetScenario> result =
            DailyFarmingBudgetScenarioCalculator.Build(
                plan,
                actions,
                Baselines(),
                currentBudget: 50,
                Now);

        DailyBudgetScenario f2p = Assert.Single(
            result,
            scenario => scenario.DailyCrystalBudget == 0);
        DailyBudgetScenario saving = Assert.Single(
            result,
            scenario => scenario.DailyCrystalBudget == 50);

        Assert.Equal(4, f2p.ModeledPortfolioDays);
        Assert.Equal(3, saving.ModeledPortfolioDays);
        Assert.Equal(1, saving.DaysSavedVsF2P);
        Assert.Equal(50, saving.CrystalsSpent);
        Assert.Equal(1, saving.RefreshCount);
        Assert.Equal(120, saving.EnergyGained);
    }

    [Fact]
    public void Build_WithUnknownBlocker_DoesNotInventPortfolioSavings()
    {
        FarmingResourcePriority signal = SignalResource(missing: 100);
        FarmingResourcePriority chromium = new(
            2,
            "chromium_transistor",
            "Chromium Transistor",
            InventoryResourceKind.RelicMaterial,
            FarmingLane.Scavenger,
            "Chatarrero",
            "test",
            120,
            0,
            120,
            0m,
            1,
            false,
            "Alta",
            [new FarmingTargetDependency("UNIT_A", "Unit A", 120)]);
        InvestmentFarmingPlan plan = Plan(
            [signal, chromium],
            [Target(blockingResourceTypes: 2)]);
        DailyFarmingAction[] actions =
        [
            SignalAction(),
            new DailyFarmingAction(
                2,
                DailyFarmingChannel.Scavenger,
                "Chatarrero",
                DailyFarmingPrecision.Guided,
                "Conversión guiada",
                "Convierte para Chromium Transistor",
                "test",
                chromium.ResourceId,
                chromium.ResourceName,
                "Chatarrero",
                null,
                null,
                120,
                1,
                false,
                "Alta",
                "test")
        ];

        IReadOnlyCollection<DailyBudgetScenario> result =
            DailyFarmingBudgetScenarioCalculator.Build(
                plan,
                actions,
                Baselines(),
                currentBudget: 300,
                Now);

        DailyBudgetScenario accelerated = Assert.Single(
            result,
            scenario => scenario.DailyCrystalBudget == 300);

        Assert.Null(accelerated.ModeledPortfolioDays);
        Assert.Null(accelerated.ModeledPortfolioCompletionAtUtc);
        Assert.Null(accelerated.DaysSavedVsF2P);
        DailyBudgetScenarioTarget target = Assert.Single(accelerated.Targets);
        Assert.False(target.FullEstimateAvailable);
        Assert.Null(target.EstimatedDays);
        Assert.Null(target.DaysSavedVsF2P);
    }

    private static InvestmentFarmingPlan Plan(
        IReadOnlyCollection<FarmingResourcePriority> resources,
        IReadOnlyCollection<FarmingTargetPlan> targets) => new(
            AllyCode,
            Now,
            Now.AddMinutes(-15),
            "test",
            targets.Count,
            targets.Count,
            0,
            resources.Count(resource => resource.Missing is > 0),
            resources.Count(resource => resource.SharedBottleneck),
            resources,
            targets);

    private static FarmingResourcePriority SignalResource(long missing) => new(
        1,
        "signal_data_fragmented",
        "Fragmented Signal Data",
        InventoryResourceKind.SignalData,
        FarmingLane.SignalData,
        "Cantina · Signal Data",
        "test",
        missing + 10,
        10,
        missing,
        0.1m,
        1,
        false,
        "Crítica",
        [new FarmingTargetDependency("UNIT_A", "Unit A", missing + 10)]);

    private static FarmingTargetPlan Target(int blockingResourceTypes = 1) => new(
        "UNIT_A",
        "Unit A",
        null,
        5,
        7,
        7,
        null,
        2,
        0,
        0.2m,
        blockingResourceTypes,
        0,
        true,
        "test");

    private static DailyFarmingAction SignalAction() => new(
        1,
        DailyFarmingChannel.CantinaEnergy,
        "Energía de Cantina",
        DailyFarmingPrecision.Exact,
        "Nodo exacto",
        "Farmea Fragmented Signal Data",
        "test",
        "signal_data_fragmented",
        "Fragmented Signal Data",
        "Cantina 8-C",
        16,
        165,
        100,
        1,
        false,
        "Crítica",
        "test");

    private static IReadOnlyCollection<DailyEnergyBaseline> Baselines() =>
    [
        new(
            DailyFarmingChannel.CantinaEnergy,
            "Cantina",
            120,
            45,
            165,
            "test"),
        new(
            DailyFarmingChannel.NormalEnergy,
            "Normal",
            240,
            135,
            375,
            "test"),
        new(
            DailyFarmingChannel.FleetEnergy,
            "Fleet",
            240,
            45,
            285,
            "test")
    ];
}
