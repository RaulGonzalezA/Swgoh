using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacJointRoundOptimizerServiceTests
{
    [Fact]
    public void EvaluateScenario_BalancedPrefersFullAttackCoverageOverMarginalDefenseGain()
    {
        GacJointRoundScenario defenseHeavy = GacJointRoundOptimizerService.EvaluateScenario(
            "defense-heavy",
            requestedDefenseSlots: 1,
            Defense(score: 90m, opportunityCost: 5m),
            Attacks(targets: 2, recommended: 0, averageScore: 0m),
            GacJointRoundOptimizationMode.Balanced);
        GacJointRoundScenario balanced = GacJointRoundOptimizerService.EvaluateScenario(
            "balanced",
            requestedDefenseSlots: 1,
            Defense(score: 70m, opportunityCost: 0m),
            Attacks(targets: 2, recommended: 2, averageScore: 80m, banners: 60m),
            GacJointRoundOptimizationMode.Balanced);

        Assert.True(balanced.JointScore > defenseHeavy.JointScore);
        Assert.Equal(100m, balanced.AttackCoverageRate);
        Assert.Equal(0m, defenseHeavy.AttackCoverageRate);
    }

    [Fact]
    public void EvaluateScenario_OffenseFirstPenalizesDefensiveOpportunityCostMoreThanDefenseFirst()
    {
        GacSmartDefenseService.SmartGeneration defense = Defense(score: 80m, opportunityCost: 20m);
        GacAttackOptimizationResult attacks = Attacks(targets: 2, recommended: 2, averageScore: 75m, banners: 58m);

        GacJointRoundScenario defenseFirst = GacJointRoundOptimizerService.EvaluateScenario(
            "same",
            1,
            defense,
            attacks,
            GacJointRoundOptimizationMode.DefenseFirst);
        GacJointRoundScenario offenseFirst = GacJointRoundOptimizerService.EvaluateScenario(
            "same",
            1,
            defense,
            attacks,
            GacJointRoundOptimizationMode.OffenseFirst);

        Assert.Equal(20m, offenseFirst.AverageDefenseOpportunityCost);
        Assert.Equal(20m, offenseFirst.OffensePreservationScore);
        Assert.NotEqual(defenseFirst.JointScore, offenseFirst.JointScore);
    }

    [Fact]
    public void EvaluateScenario_MaxBannersRewardsKnownBannerEfficiency()
    {
        GacSmartDefenseService.SmartGeneration defense = Defense(score: 75m, opportunityCost: 4m);
        GacJointRoundScenario efficient = GacJointRoundOptimizerService.EvaluateScenario(
            "efficient",
            1,
            defense,
            Attacks(targets: 2, recommended: 2, averageScore: 75m, banners: 65m),
            GacJointRoundOptimizationMode.MaxBanners);
        GacJointRoundScenario lowBanners = GacJointRoundOptimizerService.EvaluateScenario(
            "low-banners",
            1,
            defense,
            Attacks(targets: 2, recommended: 2, averageScore: 75m, banners: 48m),
            GacJointRoundOptimizationMode.MaxBanners);

        Assert.True(efficient.JointScore > lowBanners.JointScore);
        Assert.Equal(65m, efficient.KnownAverageBanners);
    }

    [Fact]
    public void EvaluateScenario_PenalizesIncompleteDefenseTemplate()
    {
        GacJointRoundScenario complete = GacJointRoundOptimizerService.EvaluateScenario(
            "complete",
            requestedDefenseSlots: 1,
            Defense(score: 70m, opportunityCost: 0m),
            Attacks(targets: 0, recommended: 0, averageScore: 0m),
            GacJointRoundOptimizationMode.Balanced);
        GacJointRoundScenario incomplete = GacJointRoundOptimizerService.EvaluateScenario(
            "incomplete",
            requestedDefenseSlots: 2,
            Defense(score: 70m, opportunityCost: 0m),
            Attacks(targets: 0, recommended: 0, averageScore: 0m),
            GacJointRoundOptimizationMode.Balanced);

        Assert.Equal(100m, complete.DefenseCompletionRate);
        Assert.Equal(50m, incomplete.DefenseCompletionRate);
        Assert.True(complete.JointScore > incomplete.JointScore);
    }

    [Fact]
    public void EvaluateScenario_KyberFiveVsFive_UsesAllFourteenRequiredDefenseSlots()
    {
        GacBoardLayout layout = GacBoardLayouts.Get(GacLeague.Kyber, GacFormat.FiveVsFive);

        GacJointRoundScenario scenario = GacJointRoundOptimizerService.EvaluateScenario(
            "kyber-regression",
            layout.TotalDefenseSlots,
            Defense(score: 70m, opportunityCost: 0m),
            Attacks(targets: 0, recommended: 0, averageScore: 0m),
            GacJointRoundOptimizationMode.Balanced);

        Assert.Equal(14, layout.TotalDefenseSlots);
        Assert.Equal(7.1m, scenario.DefenseCompletionRate);
        Assert.True(scenario.DefenseCompletionRate < 100m);
    }

    private static GacSmartDefenseService.SmartGeneration Defense(decimal score, decimal opportunityCost) => new(
        [
            new GacSmartDefenseAssignment(
                Position: 1,
                Zone: "Sur frontal",
                TeamPresetId: Guid.NewGuid(),
                TeamName: "Defense",
                Pinned: false,
                IsFleet: false,
                GalacticPower: 100_000,
                Score: score,
                DefensiveValue: score,
                OffensiveOpportunityCost: opportunityCost,
                Confidence: "Medium",
                ContainsGalacticLegend: false,
                OmicronCount: 0,
                EligibleDatacronTier: 0,
                OpponentSamples: 0,
                PersonalSamples: 0,
                Reasons: [])
        ],
        []);

    private static GacAttackOptimizationResult Attacks(
        int targets,
        int recommended,
        decimal averageScore,
        decimal? banners = null) => new(
        GacAttackOptimizationMode.RebuildPlanned,
        Applied: false,
        TargetDefenses: targets,
        RecommendedAttacks: recommended,
        HistoricalMatches: 0,
        AverageScore: averageScore,
        KnownAverageBanners: banners,
        UncoveredDefenseIds: [],
        Recommendations: [],
        SearchLimitReached: false);
}
