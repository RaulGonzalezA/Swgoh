using Swgoh.Application.Investments;

using Xunit;

namespace Swgoh.Application.UnitTests.Investments;

public sealed class RelicMaterialRequirementsTests
{
    [Fact]
    public void Calculate_FromR5ToR7_AccumulatesBothRelicSteps()
    {
        IReadOnlyDictionary<string, long> result = RelicMaterialRequirements.Calculate(5, 7);

        Assert.Equal(750_000, result["credits"]);
        Assert.Equal(40, result["carbonite_circuit_board"]);
        Assert.Equal(60, result["bronzium_wiring"]);
        Assert.Equal(50, result["chromium_transistor"]);
        Assert.Equal(40, result["aurodium_heatsink"]);
        Assert.Equal(40, result["electrium_conductor"]);
        Assert.Equal(10, result["zinbiddle_card"]);
        Assert.Equal(40, result["signal_data_fragmented"]);
        Assert.Equal(50, result["signal_data_incomplete"]);
        Assert.Equal(60, result["signal_data_flawed"]);
    }

    [Fact]
    public void Calculate_WhenTargetIsNotHigher_ReturnsNoRequirements()
    {
        IReadOnlyDictionary<string, long> result = RelicMaterialRequirements.Calculate(7, 7);

        Assert.Empty(result);
    }
}
