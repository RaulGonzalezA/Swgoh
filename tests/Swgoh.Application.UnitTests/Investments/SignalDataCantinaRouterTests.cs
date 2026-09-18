using Swgoh.Application.Investments;

using Xunit;

namespace Swgoh.Application.UnitTests.Investments;

public sealed class SignalDataCantinaRouterTests
{
    private const int CantinaBaseline = 165;

    [Fact]
    public void Build_WithSingleFragmentedDeficit_UsesDedicatedSector8Node()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            40);

        DailyFarmingAction action = Assert.Single(
            SignalDataCantinaRouter.Build([fragmented], CantinaBaseline));

        Assert.Equal("Cantina 8-C", action.Source);
        Assert.Equal(16, action.EnergyCostPerAttempt);
        Assert.Equal("signal_data_fragmented", action.ResourceId);
        Assert.Equal(40, action.Missing);
        Assert.Equal("Nodo exacto", action.PrecisionLabel);
    }

    [Fact]
    public void Build_WithFragmentedAndIncompleteDeficits_UsesSector9B()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            40);
        FarmingResourcePriority incomplete = Resource(
            "signal_data_incomplete",
            "Incomplete Signal Data",
            25);

        DailyFarmingAction action = Assert.Single(
            SignalDataCantinaRouter.Build([fragmented, incomplete], CantinaBaseline));

        Assert.Equal("Cantina 9-B", action.Source);
        Assert.Equal(20, action.EnergyCostPerAttempt);
        Assert.Equal("Nodo dual exacto", action.PrecisionLabel);
        Assert.Null(action.Missing);
        Assert.Contains("Fragmented Signal Data", action.Title, StringComparison.Ordinal);
        Assert.Contains("Incomplete Signal Data", action.Title, StringComparison.Ordinal);
        Assert.Contains("faltan 40", action.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("faltan 25", action.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithFragmentedAndFlawedDeficits_UsesSector9D()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            40);
        FarmingResourcePriority flawed = Resource(
            "signal_data_flawed",
            "Flawed Signal Data",
            35);

        DailyFarmingAction action = Assert.Single(
            SignalDataCantinaRouter.Build([fragmented, flawed], CantinaBaseline));

        Assert.Equal("Cantina 9-D", action.Source);
        Assert.Equal(20, action.EnergyCostPerAttempt);
    }

    [Fact]
    public void Build_WithIncompleteAndFlawedDeficits_UsesSector9F()
    {
        FarmingResourcePriority incomplete = Resource(
            "signal_data_incomplete",
            "Incomplete Signal Data",
            25);
        FarmingResourcePriority flawed = Resource(
            "signal_data_flawed",
            "Flawed Signal Data",
            35);

        DailyFarmingAction action = Assert.Single(
            SignalDataCantinaRouter.Build([incomplete, flawed], CantinaBaseline));

        Assert.Equal("Cantina 9-F", action.Source);
        Assert.Equal(20, action.EnergyCostPerAttempt);
    }

    [Fact]
    public void Build_WithAllSignalData_PairsHighestPriorityDeficitsAndKeepsRemainingDedicated()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            200,
            priority: "Media");
        FarmingResourcePriority incomplete = Resource(
            "signal_data_incomplete",
            "Incomplete Signal Data",
            50,
            priority: "Alta");
        FarmingResourcePriority flawed = Resource(
            "signal_data_flawed",
            "Flawed Signal Data",
            20,
            priority: "Crítica");

        IReadOnlyCollection<DailyFarmingAction> result = SignalDataCantinaRouter.Build(
            [fragmented, incomplete, flawed],
            CantinaBaseline);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, action => action.Source == "Cantina 9-F");
        DailyFarmingAction dedicated = Assert.Single(
            result,
            action => action.ResourceId == "signal_data_fragmented");
        Assert.Equal("Cantina 8-C", dedicated.Source);
    }

    [Fact]
    public void Build_WithPair_UnifiesAffectedTargetsWithoutDoubleCounting()
    {
        FarmingResourcePriority fragmented = Resource(
            "signal_data_fragmented",
            "Fragmented Signal Data",
            40,
            targets:
            [
                new FarmingTargetDependency("UNIT_A", "Unit A", 20),
                new FarmingTargetDependency("UNIT_B", "Unit B", 20)
            ]);
        FarmingResourcePriority incomplete = Resource(
            "signal_data_incomplete",
            "Incomplete Signal Data",
            25,
            targets:
            [
                new FarmingTargetDependency("UNIT_A", "Unit A", 15),
                new FarmingTargetDependency("UNIT_C", "Unit C", 10)
            ]);

        DailyFarmingAction action = Assert.Single(
            SignalDataCantinaRouter.Build([fragmented, incomplete], CantinaBaseline));

        Assert.Equal(3, action.AffectedTargetCount);
        Assert.True(action.SharedBottleneck);
    }

    private static FarmingResourcePriority Resource(
        string id,
        string name,
        long missing,
        string priority = "Alta",
        IReadOnlyCollection<FarmingTargetDependency>? targets = null)
    {
        IReadOnlyCollection<FarmingTargetDependency> dependencies = targets ??
        [
            new FarmingTargetDependency(
                $"UNIT_{id}",
                name,
                missing + 10)
        ];

        return new FarmingResourcePriority(
            1,
            id,
            name,
            InventoryResourceKind.SignalData,
            FarmingLane.SignalData,
            "Signal Data",
            "test",
            missing + 10,
            10,
            missing,
            0.1m,
            dependencies.Count,
            dependencies.Count > 1,
            priority,
            dependencies);
    }
}
