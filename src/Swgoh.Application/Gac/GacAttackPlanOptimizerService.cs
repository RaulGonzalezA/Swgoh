using Swgoh.Application.Abstractions;
using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Gac;

internal sealed class GacAttackPlanOptimizerService(
    IGacPlannerService plannerService,
    IGacRoundPlanRepository planRepository,
    IClock clock,
    IPlayerProfileService? playerProfileService = null,
    IGacPersonalLearningService? personalLearningService = null) : IGacAttackPlanOptimizerService
{
    private const int MaxCandidatesPerDefense = 6;
    private const int MaxSearchNodes = 100_000;

    public async Task<GacAttackOptimizationLookup> OptimizeCurrentAsync(
        long allyCode,
        GacAttackOptimizationMode mode,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported optimization mode.");
        }

        GacPlannerLookup plannerLookup = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (!plannerLookup.IsAvailable || plannerLookup.State is null)
        {
            return new GacAttackOptimizationLookup(
                plannerLookup.Status,
                plannerLookup.Message,
                plannerLookup.State,
                null);
        }

        GacPlannerState state = plannerLookup.State;
        GacTacticalOptimizationContext tacticalContext = await BuildTacticalContextAsync(
            allyCode,
            state.Plan.OpponentAllyCode,
            cancellationToken).ConfigureAwait(false);
        GacPersonalLearningContext personalContext = await BuildPersonalLearningContextAsync(
            allyCode,
            state.Plan.Format,
            cancellationToken).ConfigureAwait(false);
        GacAttackOptimizationResult optimization = Optimize(state, mode, tacticalContext, personalContext);
        if (!apply || optimization.Recommendations.Count == 0)
        {
            return new GacAttackOptimizationLookup(
                CurrentGacOpponentStatus.Found,
                null,
                state,
                optimization);
        }

        GacRoundPlan plan = await planRepository
            .FindByIdAsync(state.Plan.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The current GAC round plan could not be loaded for optimization.");

        List<GacAttackAssignment> retainedAttacks = mode == GacAttackOptimizationMode.RebuildPlanned
            ? [.. plan.Attacks.Where(attack => attack.Status != GacAttackPlanStatus.Planned)]
            : [.. plan.Attacks];

        foreach (GacAttackOptimizationRecommendation recommendation in optimization.Recommendations)
        {
            int attempt = retainedAttacks
                .Where(attack => attack.DefenseId == recommendation.DefenseId)
                .Select(attack => attack.Attempt)
                .DefaultIfEmpty(0)
                .Max() + 1;
            string personalNote = recommendation.PersonalSamples > 0
                ? $" personal {recommendation.PersonalAdjustment:+0.#;-0.#;0} ({recommendation.PersonalWins}/{recommendation.PersonalSamples});"
                : string.Empty;
            string notes = $"Optimizador: {recommendation.Evidence}; score {recommendation.Score:0.#}; " +
                $"coste {recommendation.StrategicCost:0.#} (reserva {recommendation.OpportunityCost:0.#}); " +
                $"ajuste táctico {recommendation.TacticalAdjustment:+0.#;-0.#;0};{personalNote} " +
                $"datacron {recommendation.DatacronStatus}.";
            retainedAttacks.Add(GacAttackAssignment.Create(
                Guid.NewGuid(),
                recommendation.DefenseId,
                recommendation.TeamPresetId,
                attempt,
                GacAttackPlanStatus.Planned,
                notes));
        }

        plan.Replace(plan.OwnDefenses, plan.VisibleDefenses, retainedAttacks, clock.UtcNow);
        await planRepository.UpsertAsync(plan, cancellationToken).ConfigureAwait(false);

        GacPlannerLookup refreshed = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        return new GacAttackOptimizationLookup(
            refreshed.Status,
            refreshed.Message,
            refreshed.State,
            optimization with { Applied = true });
    }

    internal static GacAttackOptimizationResult Optimize(
        GacPlannerState state,
        GacAttackOptimizationMode mode) =>
        Optimize(state, mode, GacTacticalOptimizationContext.Empty, GacPersonalLearningContext.Empty);

    internal static GacAttackOptimizationResult Optimize(
        GacPlannerState state,
        GacAttackOptimizationMode mode,
        GacTacticalOptimizationContext tacticalContext) =>
        Optimize(state, mode, tacticalContext, GacPersonalLearningContext.Empty);

    internal static GacAttackOptimizationResult Optimize(
        GacPlannerState state,
        GacAttackOptimizationMode mode,
        GacTacticalOptimizationContext tacticalContext,
        GacPersonalLearningContext personalContext)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(tacticalContext);
        ArgumentNullException.ThrowIfNull(personalContext);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported optimization mode.");
        }

        HashSet<string> blockedUnits = BuildBlockedUnits(state, mode);
        GacVisibleDefenseDetails[] targets =
        [
            .. state.Plan.VisibleDefenses
                .Where(defense => IsOptimizationTarget(state.Plan, defense, mode))
        ];
        if (targets.Length == 0)
        {
            return EmptyResult(mode);
        }

        GacTeamPresetDetails[] attackPresets =
        [
            .. state.Presets.Where(preset =>
                preset.Use != GacPlannerTeamUse.Defense &&
                !preset.Squad.AllUnits.Any(unit => blockedUnits.Contains(unit.DefinitionId)))
        ];
        Dictionary<bool, decimal> maxTeamPowerByType = attackPresets
            .GroupBy(preset => preset.Squad.IsFleet)
            .ToDictionary(
                group => group.Key,
                group => group.Select(TeamPower).DefaultIfEmpty(0m).Max());
        Dictionary<Guid, GacPlannerCounterHint> hints = state.Plan.CounterHints.ToDictionary(hint => hint.DefenseId);

        PreliminaryCandidate[] preliminaryCandidates =
        [
            .. targets.SelectMany(defense =>
                attackPresets
                    .Where(preset => preset.Squad.IsFleet == defense.Squad.IsFleet)
                    .Select(preset => BuildPreliminaryCandidate(
                        defense,
                        preset,
                        hints.GetValueOrDefault(defense.Id),
                        maxTeamPowerByType.GetValueOrDefault(defense.Squad.IsFleet),
                        tacticalContext,
                        personalContext)))
        ];
        IReadOnlyDictionary<GacStrategicCandidateKey, GacOpportunityAssessment> opportunityAssessments =
            GacStrategicOpportunityEvaluator.Evaluate(
                [
                    .. preliminaryCandidates.Select(candidate => new GacStrategicCandidateSnapshot(
                        candidate.Defense.Id,
                        candidate.Preset.Id,
                        candidate.ScoreBeforeOpportunity,
                        candidate.UnitDefinitionIds))
                ]);

        DefenseChoice[] choices =
        [
            .. targets.Select(defense => new DefenseChoice(
                defense,
                [
                    .. preliminaryCandidates
                        .Where(candidate => candidate.Defense.Id == defense.Id)
                        .Select(candidate => FinalizeCandidate(
                            candidate,
                            opportunityAssessments.GetValueOrDefault(
                                new GacStrategicCandidateKey(candidate.Defense.Id, candidate.Preset.Id))
                            ?? GacOpportunityAssessment.None))
                        .Where(candidate => candidate is not null)
                        .Select(candidate => candidate!)
                        .OrderByDescending(candidate => candidate.Score)
                        .ThenBy(candidate => candidate.StrategicCost)
                        .Take(MaxCandidatesPerDefense)
                ]))
                .OrderBy(choice => choice.Candidates.Count)
                .ThenByDescending(choice => choice.Candidates.FirstOrDefault()?.Score ?? 0m)
        ];

        var search = new SearchState();
        Search(
            choices,
            index: 0,
            selected: [],
            usedUnits: [],
            totalScore: 0m,
            totalCost: 0m,
            search);

        GacAttackOptimizationRecommendation[] recommendations =
        [
            .. search.BestCandidates
                .Select(ToRecommendation)
                .OrderBy(recommendation => recommendation.Zone, StringComparer.OrdinalIgnoreCase)
                .ThenBy(recommendation => recommendation.DefenseName, StringComparer.OrdinalIgnoreCase)
        ];
        HashSet<Guid> coveredDefenseIds = recommendations
            .Select(recommendation => recommendation.DefenseId)
            .ToHashSet();
        Guid[] uncoveredDefenseIds =
        [
            .. targets
                .Where(defense => !coveredDefenseIds.Contains(defense.Id))
                .Select(defense => defense.Id)
        ];
        decimal[] knownBanners =
        [
            .. recommendations
                .Where(recommendation => recommendation.AverageBanners is not null)
                .Select(recommendation => recommendation.AverageBanners!.Value)
        ];

        return new GacAttackOptimizationResult(
            mode,
            Applied: false,
            targets.Length,
            recommendations.Length,
            recommendations.Count(IsHistorical),
            recommendations.Length == 0 ? 0m : Math.Round(recommendations.Average(item => item.Score), 1),
            knownBanners.Length == 0 ? null : Math.Round(knownBanners.Average(), 1),
            uncoveredDefenseIds,
            recommendations,
            search.SearchLimitReached);
    }

    private async Task<GacTacticalOptimizationContext> BuildTacticalContextAsync(
        long allyCode,
        long opponentAllyCode,
        CancellationToken cancellationToken)
    {
        if (playerProfileService is null)
        {
            return GacTacticalOptimizationContext.Empty;
        }

        Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(allyCode, cancellationToken);
        Task<PlayerProfile?> opponentTask = playerProfileService.GetAsync(opponentAllyCode, cancellationToken);
        await Task.WhenAll(playerTask, opponentTask).ConfigureAwait(false);
        return GacTacticalOptimizationContext.From(
            await playerTask.ConfigureAwait(false),
            await opponentTask.ConfigureAwait(false));
    }

    private async Task<GacPersonalLearningContext> BuildPersonalLearningContextAsync(
        long allyCode,
        GacFormat format,
        CancellationToken cancellationToken)
    {
        if (personalLearningService is null)
        {
            return GacPersonalLearningContext.Empty;
        }

        IReadOnlyCollection<GacPersonalMatchupStatistics> statistics = await personalLearningService
            .GetStatisticsAsync(allyCode, format, cancellationToken)
            .ConfigureAwait(false);
        return GacPersonalLearningContext.From(statistics);
    }

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
        SearchState state)
    {
        state.NodesVisited++;
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
                state);

            selected.RemoveAt(selected.Count - 1);
            RebuildUsedUnits(selected, usedUnits);
        }

        Search(choices, index + 1, selected, usedUnits, totalScore, totalCost, state);
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
        candidate.PersonalRationale);

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
        SearchLimitReached: false);

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
