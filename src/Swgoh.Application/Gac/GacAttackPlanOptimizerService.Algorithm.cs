using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed partial class GacAttackPlanOptimizerService
{
    private static HashSet<string> BuildBlockedUnits(
        GacPlannerState state,
        GacAttackOptimizationMode mode)
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GacOwnDefenseAssignmentDetails defense in state.Plan.OwnDefenses)
        {
            AddUnits(blocked, defense.Team.Squad);
        }

        foreach (GacAttackAssignmentDetails attack in state.Plan.Attacks)
        {
            bool consumesUnits = attack.Status is GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed ||
                (mode == GacAttackOptimizationMode.FillGaps && attack.Status == GacAttackPlanStatus.Planned);
            if (consumesUnits)
            {
                AddUnits(blocked, attack.Team.Squad);
            }
        }

        return blocked;
    }

    private static bool IsOptimizationTarget(
        GacRoundPlanDetails plan,
        GacVisibleDefenseDetails defense,
        GacAttackOptimizationMode mode)
    {
        if (defense.Defeated)
        {
            return false;
        }

        return mode == GacAttackOptimizationMode.RebuildPlanned ||
            !plan.Attacks.Any(attack =>
                attack.DefenseId == defense.Id &&
                attack.Status == GacAttackPlanStatus.Planned);
    }

    private static PreliminaryCandidate BuildPreliminaryCandidate(
        GacVisibleDefenseDetails defense,
        GacTeamPresetDetails preset,
        GacPlannerCounterHint? hint,
        decimal maxTeamPower,
        GacTacticalOptimizationContext tacticalContext,
        GacPersonalLearningContext personalContext)
    {
        decimal teamPower = TeamPower(preset);
        decimal defensePower = SquadPower(defense.Squad);
        (decimal matchScore, string evidence, string confidence, string rationale) =
            ScoreMatch(defense, preset, hint, teamPower, defensePower);
        decimal baseStrategicCost = CalculateBaseStrategicCost(preset, teamPower, defensePower, maxTeamPower);
        GacTacticalEvaluation tactical = tacticalContext.Evaluate(
            defense,
            preset,
            hint?.RequiresDatacronVerification == true);
        GacPersonalLearningSignal personal = personalContext.Evaluate(defense, preset);
        decimal scoreBeforeOpportunity = Math.Clamp(
            matchScore - baseStrategicCost + tactical.Adjustment + personal.Adjustment,
            0m,
            100m);
        string adjustedConfidence = tactical.DatacronStatus == "NoCandidate"
            ? DowngradeConfidence(confidence)
            : confidence;
        string enrichedRationale = tactical.Summary.StartsWith("Sin datos", StringComparison.Ordinal)
            ? rationale
            : $"{rationale} {tactical.Summary}";
        if (personal.Samples > 0)
        {
            enrichedRationale = $"{enrichedRationale} {personal.Summary}";
        }

        return new PreliminaryCandidate(
            defense,
            preset,
            scoreBeforeOpportunity,
            baseStrategicCost,
            evidence,
            adjustedConfidence,
            enrichedRationale,
            hint?.WinRate,
            hint?.OneShotRate,
            hint?.AverageBanners,
            hint?.Uses,
            tactical.Adjustment,
            tactical.TeamAverageSpeed,
            tactical.DefenseAverageSpeed,
            tactical.TeamModSpeedBonus,
            tactical.DefenseModSpeedBonus,
            tactical.DatacronStatus,
            personal.Adjustment,
            personal.Samples,
            personal.Wins,
            personal.WinRate,
            personal.OneShotRate,
            personal.AverageBanners,
            personal.Scope,
            personal.Summary,
            [.. preset.Squad.AllUnits.Select(unit => unit.DefinitionId)]);
    }

    private static Candidate? FinalizeCandidate(
        PreliminaryCandidate preliminary,
        GacOpportunityAssessment opportunity)
    {
        decimal strategicCost = Math.Round(
            Math.Clamp(preliminary.BaseStrategicCost + opportunity.OpportunityCost, 0m, 35m),
            1);
        decimal score = Math.Clamp(
            preliminary.ScoreBeforeOpportunity - opportunity.OpportunityCost,
            0m,
            100m);
        if (score < 25m)
        {
            return null;
        }

        string rationale = opportunity.OpportunityCost > 0m || opportunity.FutureDefensesAtRisk > 0
            ? $"{preliminary.Rationale} {opportunity.Summary}"
            : preliminary.Rationale;

        return new Candidate(
            preliminary.Defense,
            preliminary.Preset,
            score,
            strategicCost,
            preliminary.BaseStrategicCost,
            opportunity.OpportunityCost,
            opportunity.AlternativesHere,
            opportunity.FutureDefensesAtRisk,
            opportunity.Summary,
            preliminary.Evidence,
            preliminary.Confidence,
            rationale,
            preliminary.WinRate,
            preliminary.OneShotRate,
            preliminary.AverageBanners,
            preliminary.Uses,
            preliminary.TacticalAdjustment,
            preliminary.TeamAverageSpeed,
            preliminary.DefenseAverageSpeed,
            preliminary.TeamModSpeedBonus,
            preliminary.DefenseModSpeedBonus,
            preliminary.DatacronStatus,
            preliminary.PersonalAdjustment,
            preliminary.PersonalSamples,
            preliminary.PersonalWins,
            preliminary.PersonalWinRate,
            preliminary.PersonalOneShotRate,
            preliminary.PersonalAverageBanners,
            preliminary.PersonalScope,
            preliminary.PersonalRationale,
            preliminary.UnitDefinitionIds);
    }

    private static (decimal Score, string Evidence, string Confidence, string Rationale) ScoreMatch(
        GacVisibleDefenseDetails defense,
        GacTeamPresetDetails preset,
        GacPlannerCounterHint? hint,
        decimal teamPower,
        decimal defensePower)
    {
        if (hint is not null)
        {
            HashSet<string> presetUnits = preset.Squad.AllUnits
                .Select(unit => unit.DefinitionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] recommendedIds =
            [
                .. hint.RecommendedTeam
                    .Select(unit => unit.DefinitionId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
            bool directMatch = hint.MatchingTeamPresetId == preset.Id ||
                (recommendedIds.Length > 1 && recommendedIds.All(presetUnits.Contains));
            if (directMatch)
            {
                decimal score = DirectCounterScore(hint);
                string evidence = HasHistoricalEvidence(hint) ? "Histórico observado" : "Counter War Room";
                return (
                    score,
                    evidence,
                    hint.Confidence,
                    $"{hint.Rationale} El equipo guardado encaja con el counter recomendado.");
            }

            if (recommendedIds.Length > 0)
            {
                int overlap = recommendedIds.Count(presetUnits.Contains);
                decimal similarity = overlap / (decimal)recommendedIds.Length;
                if (similarity >= 0.5m)
                {
                    decimal score = 50m + (similarity * 18m);
                    if (HasHistoricalEvidence(hint))
                    {
                        score += 4m;
                    }

                    return (
                        Math.Clamp(score, 0m, 78m),
                        "Counter compatible",
                        DowngradeConfidence(hint.Confidence),
                        $"Comparte {overlap}/{recommendedIds.Length} piezas del counter recomendado por la War Room.");
                }
            }
        }

        if (teamPower <= 0m || defensePower <= 0m)
        {
            return (
                38m,
                "Estimación por roster",
                "Low",
                "No hay evidencia histórica suficiente; se conserva como alternativa de baja confianza.");
        }

        decimal ratio = teamPower / defensePower;
        decimal scoreByPower = 44m + Math.Clamp((ratio - 0.85m) * 30m, -12m, 18m);
        return (
            Math.Clamp(scoreByPower, 28m, 62m),
            "Estimación por roster",
            "Low",
            $"Estimación por fuerza relativa del equipo ({teamPower:N0} GP vs {defensePower:N0} GP). Verifica sinergias, mods y datacron.");
    }

    private static decimal DirectCounterScore(GacPlannerCounterHint hint)
    {
        decimal score = hint.Confidence switch
        {
            "High" => 78m,
            "Medium" => 70m,
            _ => 62m
        };

        if (hint.WinRate is decimal winRate)
        {
            score = 60m + (NormalizeRate(winRate) * 28m);
        }

        if (hint.OneShotRate is decimal oneShotRate)
        {
            score += NormalizeRate(oneShotRate) * 6m;
        }

        if (hint.Uses is int uses)
        {
            score += Math.Min(6m, (decimal)Math.Log10(Math.Max(1, uses)) * 3m);
        }

        if (string.Equals(hint.Source, "RosterStrengthHeuristic", StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Min(score, 66m);
        }

        return Math.Clamp(score, 0m, 98m);
    }

    private static decimal CalculateBaseStrategicCost(
        GacTeamPresetDetails preset,
        decimal teamPower,
        decimal defensePower,
        decimal maxTeamPower)
    {
        decimal powerCost = maxTeamPower <= 0m ? 4m : (teamPower / maxTeamPower) * 9m;
        decimal flexibilityCost = preset.Use == GacPlannerTeamUse.Flexible ? 1.5m : 0m;
        decimal overkillCost = 0m;
        if (teamPower > 0m && defensePower > 0m)
        {
            decimal ratio = teamPower / defensePower;
            if (ratio > 1.35m)
            {
                overkillCost = Math.Min(10m, (ratio - 1.35m) * 8m);
            }
        }

        return Math.Round(Math.Clamp(powerCost + flexibilityCost + overkillCost, 0m, 20m), 1);
    }

    private static void Search(
        IReadOnlyList<DefenseChoice> choices,
        int index,
        List<Candidate> selected,
        HashSet<string> usedUnits,
        decimal totalScore,
        decimal totalCost,
        SearchState state,
        CancellationToken cancellationToken)
    {
        state.NodesVisited++;
        if ((state.NodesVisited & 0xFF) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (state.NodesVisited > MaxSearchNodes)
        {
            state.SearchLimitReached = true;
            return;
        }

        if (selected.Count + (choices.Count - index) < state.BestCandidates.Count)
        {
            return;
        }

        if (index == choices.Count)
        {
            state.Consider(selected, totalScore, totalCost);
            return;
        }

        DefenseChoice choice = choices[index];
        foreach (Candidate candidate in choice.Candidates)
        {
            if (candidate.UnitDefinitionIds.Any(usedUnits.Contains))
            {
                continue;
            }

            selected.Add(candidate);
            foreach (string unitId in candidate.UnitDefinitionIds)
            {
                usedUnits.Add(unitId);
            }

            Search(
                choices,
                index + 1,
                selected,
                usedUnits,
                totalScore + candidate.Score,
                totalCost + candidate.StrategicCost,
                state,
                cancellationToken);

            selected.RemoveAt(selected.Count - 1);
            RebuildUsedUnits(selected, usedUnits);
        }

        Search(
            choices,
            index + 1,
            selected,
            usedUnits,
            totalScore,
            totalCost,
            state,
            cancellationToken);
    }

    private static void RebuildUsedUnits(IEnumerable<Candidate> selected, HashSet<string> usedUnits)
    {
        usedUnits.Clear();
        foreach (Candidate candidate in selected)
        {
            foreach (string unitId in candidate.UnitDefinitionIds)
            {
                usedUnits.Add(unitId);
            }
        }
    }

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
