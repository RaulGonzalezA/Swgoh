using Swgoh.Application.Investments;

using Xunit;

namespace Swgoh.Application.UnitTests.Investments;

public sealed class InvestmentCostEstimatorTests
{
    [Fact]
    public void Estimate_ReturnsNullWithoutConcreteUpgrade()
    {
        InvestmentCostEstimate? result = InvestmentCostEstimator.Estimate(7, 7, null, null);

        Assert.Null(result);
    }

    [Fact]
    public void Estimate_MakesLateRelicStepsMoreExpensive()
    {
        InvestmentCostEstimate? early = InvestmentCostEstimator.Estimate(3, 7, 4, null);
        InvestmentCostEstimate? late = InvestmentCostEstimator.Estimate(7, 7, 8, null);

        Assert.NotNull(early);
        Assert.NotNull(late);
        Assert.True(late.CostIndex > early.CostIndex);
        Assert.True(late.IsEstimate);
    }
}
