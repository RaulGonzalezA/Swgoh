using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed partial class GacAttackPlanOptimizerService
{
    private static GacCounterDefenseAnalysis ToCounterDefenseAnalysis(DefenseChoice choice)
    {
        GacCounterCandidateAnalysis[] candidates =
        [
            .. choice.Candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.StrategicCost)
                .Take(5)
                .Select((candidate, index) => ToCounterCandidateAnalysis(candidate, index + 1))
        ];

        return new GacCounterDefenseAnalysis(
            choice.Defense.Id,
            choice.Defense.Squad.Leader.Name,
            choice.Defense.Zone,
            candidates);
    }

    private static GacCounterCandidateAnalysis ToCounterCandidateAnalysis(Candidate candidate, int rank) => new(
        rank,
        candidate.Preset.Id,
        candidate.Preset.Name,
        Math.Round(candidate.Score, 1),
        EstimateWinProbability(candidate),
        ExpectedBanners(candidate),
        AssessRisk(candidate),
        AssessTimeoutRisk(candidate),
        candidate.StrategicCost,
        CriticalPieceCost(candidate),
        candidate.Evidence,
        candidate.Confidence,
        candidate.Rationale,
        candidate.DatacronStatus,
        candidate.TacticalAdjustment,
        candidate.PersonalAdjustment,
        candidate.FutureDefensesAtRisk);

    private static GacAttackOptimizationRecommendation ToRecommendation(Candidate candidate) => new(
        candidate.Defense.Id,
        candidate.Defense.Squad.Leader.Name,
        candidate.Defense.Zone,
        candidate.Preset.Id,
        candidate.Preset.Name,
        Math.Round(candidate.Score, 1),
        candidate.StrategicCost,
        candidate.Evidence,
        candidate.Confidence,
        candidate.Rationale,
        candidate.WinRate,
        candidate.OneShotRate,
        candidate.AverageBanners,
        candidate.Uses,
        candidate.TacticalAdjustment,
        candidate.TeamAverageSpeed,
        candidate.DefenseAverageSpeed,
        candidate.TeamModSpeedBonus,
        candidate.DefenseModSpeedBonus,
        candidate.DatacronStatus,
        candidate.BaseStrategicCost,
        candidate.OpportunityCost,
        candidate.StrategicAlternatives,
        candidate.FutureDefensesAtRisk,
        candidate.StrategicRationale,
        candidate.PersonalAdjustment,
        candidate.PersonalSamples,
        candidate.PersonalWins,
        candidate.PersonalWinRate,
        candidate.PersonalOneShotRate,
        candidate.PersonalAverageBanners,
        candidate.PersonalScope,
        candidate.PersonalRationale,
        EstimateWinProbability(candidate),
        AssessRisk(candidate),
        AssessTimeoutRisk(candidate),
        CriticalPieceCost(candidate));

    private static decimal EstimateWinProbability(Candidate candidate)
    {
        decimal weightedTotal = 0m;
        decimal weight = 0m;
        if (candidate.WinRate is decimal historicalWinRate)
        {
            decimal historicalWeight = Math.Clamp(candidate.Uses ?? 1, 1, 20);
            weightedTotal += NormalizeRate(historicalWinRate) * 100m * historicalWeight;
            weight += historicalWeight;
        }

        if (candidate.PersonalWinRate is decimal personalWinRate && candidate.PersonalSamples > 0)
        {
            decimal personalWeight = Math.Clamp(candidate.PersonalSamples * 2m, 2m, 20m);
            weightedTotal += NormalizeRate(personalWinRate) * 100m * personalWeight;
            weight += personalWeight;
        }

        decimal estimate;
        if (weight > 0m)
        {
            estimate = weightedTotal / weight;
            estimate += candidate.TacticalAdjustment * 0.6m;
        }
        else
        {
            estimate = 34m + (candidate.Score * 0.62m) + (candidate.TacticalAdjustment * 0.8m);
            if (string.Equals(candidate.Confidence, "Low", StringComparison.OrdinalIgnoreCase))
            {
                estimate = Math.Min(estimate, 78m);
            }
        }

        if (string.Equals(candidate.DatacronStatus, "NoCandidate", StringComparison.OrdinalIgnoreCase))
        {
            estimate -= 8m;
        }

        return Math.Round(Math.Clamp(estimate, 15m, 98m), 1);
    }

    private static decimal? ExpectedBanners(Candidate candidate)
    {
        decimal? value = candidate.PersonalSamples >= 3
            ? candidate.PersonalAverageBanners ?? candidate.AverageBanners
            : candidate.AverageBanners ?? candidate.PersonalAverageBanners;
        return value is null ? null : Math.Round(value.Value, 1);
    }

    private static string AssessRisk(Candidate candidate)
    {
        decimal winProbability = EstimateWinProbability(candidate);
        if (string.Equals(candidate.DatacronStatus, "NoCandidate", StringComparison.OrdinalIgnoreCase) ||
            winProbability < 58m)
        {
            return "High";
        }

        if (winProbability < 78m ||
            (string.Equals(candidate.Confidence, "Low", StringComparison.OrdinalIgnoreCase) && candidate.Uses is null))
        {
            return "Medium";
        }

        return "Low";
    }

    private static string AssessTimeoutRisk(Candidate candidate)
    {
        decimal? oneShotRate = candidate.PersonalSamples >= 3
            ? candidate.PersonalOneShotRate ?? candidate.OneShotRate
            : candidate.OneShotRate ?? candidate.PersonalOneShotRate;
        if (oneShotRate is decimal observedOneShot)
        {
            decimal normalized = NormalizeRate(observedOneShot) * 100m;
            return normalized switch
            {
                < 55m => "High",
                < 78m => "Medium",
                _ => "Low"
            };
        }

        if (candidate.TeamAverageSpeed is decimal teamSpeed &&
            candidate.DefenseAverageSpeed is decimal defenseSpeed &&
            defenseSpeed - teamSpeed >= 25m)
        {
            return "High";
        }

        return candidate.Score switch
        {
            < 50m => "High",
            < 72m => "Medium",
            _ => "Low"
        };
    }

    private static decimal CriticalPieceCost(Candidate candidate) => Math.Round(
        Math.Clamp(candidate.OpportunityCost + (candidate.FutureDefensesAtRisk * 4m), 0m, 30m),
        1);

    private static bool HasHistoricalEvidence(GacPlannerCounterHint hint) =>
        hint.WinRate is not null || hint.Uses is > 0 ||
        hint.Source.Contains("Historical", StringComparison.OrdinalIgnoreCase) ||
        hint.Source.Contains("Observed", StringComparison.OrdinalIgnoreCase);

    private static bool IsHistorical(GacAttackOptimizationRecommendation recommendation) =>
        recommendation.WinRate is not null || recommendation.Uses is > 0 ||
        recommendation.Evidence.Contains("Histórico", StringComparison.OrdinalIgnoreCase);

    private static string DowngradeConfidence(string confidence) => confidence switch
    {
        "High" => "Medium",
        "Medium" => "Low",
        _ => "Low"
    };

    private static decimal NormalizeRate(decimal value) =>
        Math.Clamp(value <= 1m ? value : value / 100m, 0m, 1m);

    private static decimal TeamPower(GacTeamPresetDetails preset) => SquadPower(preset.Squad);

    private static decimal SquadPower(GacPlannerSquadDetails squad) =>
        squad.AllUnits.Sum(unit => unit.GalacticPower ?? 0L);

    private static void AddUnits(HashSet<string> target, GacPlannerSquadDetails squad)
    {
        foreach (GacPlannerUnitDetails unit in squad.AllUnits)
        {
            target.Add(unit.DefinitionId);
        }
    }

    private static GacAttackOptimizationResult EmptyResult(GacAttackOptimizationMode mode) => new(
        mode,
        Applied: false,
        TargetDefenses: 0,
        RecommendedAttacks: 0,
        HistoricalMatches: 0,
        AverageScore: 0m,
        KnownAverageBanners: null,
        UncoveredDefenseIds: [],
        Recommendations: [],
        SearchLimitReached: false,
        CounterAnalyses: []);

    private sealed record PreliminaryCandidate(
        GacVisibleDefenseDetails Defense,
        GacTeamPresetDetails Preset,
        decimal ScoreBeforeOpportunity,
        decimal BaseStrategicCost,
        string Evidence,
        string Confidence,
        string Rationale,
        decimal? WinRate,
        decimal? OneShotRate,
        decimal? AverageBanners,
        int? Uses,
        decimal TacticalAdjustment,
        decimal? TeamAverageSpeed,
        decimal? DefenseAverageSpeed,
        decimal? TeamModSpeedBonus,
        decimal? DefenseModSpeedBonus,
        string DatacronStatus,
        decimal PersonalAdjustment,
        int PersonalSamples,
        int PersonalWins,
        decimal? PersonalWinRate,
        decimal? PersonalOneShotRate,
        decimal? PersonalAverageBanners,
        string PersonalScope,
        string PersonalRationale,
        IReadOnlyCollection<string> UnitDefinitionIds);

    private sealed record Candidate(
        GacVisibleDefenseDetails Defense,
        GacTeamPresetDetails Preset,
        decimal Score,
        decimal StrategicCost,
        decimal BaseStrategicCost,
        decimal OpportunityCost,
        int StrategicAlternatives,
        int FutureDefensesAtRisk,
        string StrategicRationale,
        string Evidence,
        string Confidence,
        string Rationale,
        decimal? WinRate,
        decimal? OneShotRate,
        decimal? AverageBanners,
        int? Uses,
        decimal TacticalAdjustment,
        decimal? TeamAverageSpeed,
        decimal? DefenseAverageSpeed,
        decimal? TeamModSpeedBonus,
        decimal? DefenseModSpeedBonus,
        string DatacronStatus,
        decimal PersonalAdjustment,
        int PersonalSamples,
        int PersonalWins,
        decimal? PersonalWinRate,
        decimal? PersonalOneShotRate,
        decimal? PersonalAverageBanners,
        string PersonalScope,
        string PersonalRationale,
        IReadOnlyCollection<string> UnitDefinitionIds);

    private sealed record DefenseChoice(
        GacVisibleDefenseDetails Defense,
        IReadOnlyCollection<Candidate> Candidates);

    private sealed class SearchState
    {
        public int NodesVisited { get; set; }
        public bool SearchLimitReached { get; set; }
        public IReadOnlyCollection<Candidate> BestCandidates { get; private set; } = [];
        private decimal BestScore { get; set; } = decimal.MinValue;
        private decimal BestCost { get; set; } = decimal.MaxValue;

        public void Consider(IReadOnlyCollection<Candidate> selected, decimal totalScore, decimal totalCost)
        {
            bool betterCoverage = selected.Count > BestCandidates.Count;
            bool sameCoverageBetterScore = selected.Count == BestCandidates.Count && totalScore > BestScore;
            bool sameScoreLowerCost = selected.Count == BestCandidates.Count &&
                totalScore == BestScore &&
                totalCost < BestCost;
            if (!betterCoverage && !sameCoverageBetterScore && !sameScoreLowerCost)
            {
                return;
            }

            BestCandidates = [.. selected];
            BestScore = totalScore;
            BestCost = totalCost;
        }
    }
}
