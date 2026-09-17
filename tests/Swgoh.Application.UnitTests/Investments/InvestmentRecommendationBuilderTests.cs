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
    }

    [Fact]
    public void Build_UsesVerifiedRelicTargetInSuggestedAction()
    {
        InvestmentSignal[] signals =
        [
            new InvestmentSignal(
                "UNIT_A",
                "Unit A",
                "tex.unit_a",
                5,
                7,
                InvestmentModule.RiseOfEmpire,
                40m,
                "Necesaria para una misión especial.",
                TargetRelicTier: 7,
                ConcreteTarget: true)
        ];

        InvestmentRecommendation result = Assert.Single(InvestmentRecommendationBuilder.Build(signals));

        Assert.Equal(7, result.TargetRelicTier);
        Assert.Equal("R5 → R7", result.SuggestedAction);
        Assert.True(result.HasConcreteTarget);
        Assert.Equal(45m, result.Score);
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
}
