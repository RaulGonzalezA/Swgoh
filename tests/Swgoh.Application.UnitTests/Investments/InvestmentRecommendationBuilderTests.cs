using Swgoh.Application.Investments;

using Xunit;

namespace Swgoh.Application.UnitTests.Investments;

public sealed class InvestmentRecommendationBuilderTests
{
    [Fact]
    public void Build_MergesModulesAndAddsCrossModuleValue()
    {
        InvestmentSignal[] signals =
        [
            Signal("UNIT_A", InvestmentModule.Gac, 17m),
            Signal("UNIT_A", InvestmentModule.Conquest, 12m),
            Signal("UNIT_B", InvestmentModule.Gac, 25m)
        ];

        IReadOnlyCollection<InvestmentRecommendation> result = InvestmentRecommendationBuilder.Build(signals);

        InvestmentRecommendation first = result.First();
        Assert.Equal("UNIT_A", first.DefinitionId);
        Assert.Equal(2, first.ModuleCount);
        Assert.Equal(37m, first.Score);
        Assert.Equal("Media", first.Priority);
        Assert.Equal("Estratégica", first.ValueRating);
        Assert.Null(first.EstimatedCost);
    }

    [Fact]
    public void Build_UsesVerifiedRelicTargetAndAddsEstimatedCost()
    {
        InvestmentSignal[] signals =
        [
            RelicSignal("UNIT_A", 5, 7, 40m)
        ];

        InvestmentRecommendation result = Assert.Single(InvestmentRecommendationBuilder.Build(signals));

        Assert.Equal(7, result.TargetRelicTier);
        Assert.Equal("R5 → R7", result.SuggestedAction);
        Assert.True(result.HasConcreteTarget);
        Assert.Equal(45m, result.Score);
        Assert.NotNull(result.EstimatedCost);
        Assert.Equal(2, result.EstimatedCost.RelicSteps);
        Assert.True(result.EstimatedCost.CostIndex > 0m);
        Assert.NotNull(result.ImpactPerCost);
        Assert.False(result.UsesRealInventory);
    }

    [Fact]
    public void Build_UsesNextStarGateInsteadOfInventingRelics()
    {
        InvestmentSignal[] signals =
        [
            new InvestmentSignal(
                "ERA:UNIT_A",
                "Unit A",
                null,
                0,
                3,
                InvestmentModule.Era,
                34m,
                "Bloquea el siguiente tier.",
                TargetStars: 5,
                ConcreteTarget: true),
            Signal("ERA:UNIT_A", InvestmentModule.Coliseum, 6m, currentStars: 3)
        ];

        InvestmentRecommendation result = Assert.Single(InvestmentRecommendationBuilder.Build(signals));

        Assert.Null(result.TargetRelicTier);
        Assert.Equal(5, result.TargetStars);
        Assert.Equal("3★ → 5★", result.SuggestedAction);
        Assert.Equal(53m, result.Score);
        Assert.Equal("Alta", result.Priority);
        Assert.NotNull(result.EstimatedCost);
        Assert.Equal(2, result.EstimatedCost.StarSteps);
        Assert.Null(result.Inventory);
    }

    [Fact]
    public void Build_PrefersCheaperUpgradeWhenImpactIsSimilar()
    {
        InvestmentSignal[] signals =
        [
            RelicSignal("CHEAP", 6, 7, 40m),
            RelicSignal("EXPENSIVE", 3, 7, 42m)
        ];

        IReadOnlyCollection<InvestmentRecommendation> result = InvestmentRecommendationBuilder.Build(signals);

        Assert.Equal("CHEAP", result.First().DefinitionId);
        Assert.True(result.First().ImpactPerCost > result.Last().ImpactPerCost);
        Assert.True(result.First().ValueScore > result.Last().ValueScore);
    }

    [Fact]
    public void Build_WithCompleteInventory_MarksUpgradeReadyNowAndBoostsValue()
    {
        InvestmentSignal[] signals = [RelicSignal("READY", 5, 7, 40m)];
        PlayerInventorySnapshot inventory = R5ToR7Inventory(complete: true);

        InvestmentRecommendation result = Assert.Single(InvestmentRecommendationBuilder.Build(signals, inventory: inventory));

        Assert.True(result.UsesRealInventory);
        Assert.True(result.CanCompleteNow);
        Assert.NotNull(result.Inventory);
        Assert.Equal(1m, result.Inventory.Coverage);
        Assert.Empty(result.Inventory.Resources.Where(resource => !resource.Sufficient));
        Assert.True(result.InventoryAdjustedValueScore > result.ValueScore);
    }

    [Fact]
    public void Build_WithIncompleteInventory_ReportsExactMissingResources()
    {
        InvestmentSignal[] signals = [RelicSignal("BLOCKED", 5, 7, 40m)];
        PlayerInventorySnapshot inventory = R5ToR7Inventory(complete: false);

        InvestmentRecommendation result = Assert.Single(InvestmentRecommendationBuilder.Build(signals, inventory: inventory));

        Assert.False(result.CanCompleteNow);
        Assert.NotNull(result.Inventory);
        Assert.Equal(2, result.Inventory.MissingResourceTypes);
        InvestmentResourceNeed electrium = Assert.Single(result.Inventory.Resources.Where(resource => resource.ResourceId == "electrium_conductor"));
        InvestmentResourceNeed zinbiddle = Assert.Single(result.Inventory.Resources.Where(resource => resource.ResourceId == "zinbiddle_card"));
        Assert.Equal(30, electrium.Missing);
        Assert.Equal(10, zinbiddle.Missing);
        Assert.True(result.Inventory.Coverage < 1m);
        Assert.True(result.InventoryAdjustedValueScore < result.ValueScore);
    }

    private static InvestmentSignal Signal(
        string definitionId,
        InvestmentModule module,
        decimal score,
        int currentStars = 0) => new(
        definitionId,
        definitionId.Replace('_', ' '),
        null,
        0,
        currentStars,
        module,
        score,
        $"Signal from {module}.");

    private static InvestmentSignal RelicSignal(string definitionId, int currentRelic, int targetRelic, decimal score) => new(
        definitionId,
        definitionId.Replace('_', ' '),
        null,
        currentRelic,
        7,
        InvestmentModule.RiseOfEmpire,
        score,
        "Necesaria para una misión especial.",
        TargetRelicTier: targetRelic,
        ConcreteTarget: true);

    private static PlayerInventorySnapshot R5ToR7Inventory(bool complete)
    {
        var quantities = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["credits"] = 750_000,
            ["carbonite_circuit_board"] = 40,
            ["bronzium_wiring"] = 60,
            ["chromium_transistor"] = 50,
            ["aurodium_heatsink"] = 40,
            ["electrium_conductor"] = complete ? 40 : 10,
            ["zinbiddle_card"] = complete ? 10 : 0,
            ["signal_data_fragmented"] = 40,
            ["signal_data_incomplete"] = 50,
            ["signal_data_flawed"] = 60
        };

        return new PlayerInventorySnapshot(
            123_456_789,
            DateTimeOffset.Parse("2026-09-17T12:00:00Z"),
            "test",
            [
                .. PlayerInventoryCatalog.Resources.Select(resource => new PlayerInventoryResource(
                    resource.Id,
                    resource.Name,
                    quantities.GetValueOrDefault(resource.Id)))
            ]);
    }
}
