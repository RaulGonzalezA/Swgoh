using Swgoh.Application.Abstractions;
using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Gac;

internal sealed partial class GacAttackPlanOptimizerService(
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
        GacAttackOptimizationResult optimization = Optimize(
            state,
            mode,
            tacticalContext,
            personalContext,
            cancellationToken);
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
            string notes = $"Counter Engine 2.0: {recommendation.Evidence}; score {recommendation.Score:0.#}; " +
                $"win estimado {recommendation.EstimatedWinProbability:0.#}%; riesgo {recommendation.Risk}; " +
                $"timeout {recommendation.TimeoutRisk}; coste {recommendation.StrategicCost:0.#} " +
                $"(piezas críticas {recommendation.CriticalPieceCost:0.#}); " +
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
        Optimize(
            state,
            mode,
            GacTacticalOptimizationContext.Empty,
            GacPersonalLearningContext.Empty,
            CancellationToken.None);

    internal static GacAttackOptimizationResult Optimize(
        GacPlannerState state,
        GacAttackOptimizationMode mode,
        GacTacticalOptimizationContext tacticalContext) =>
        Optimize(
            state,
            mode,
            tacticalContext,
            GacPersonalLearningContext.Empty,
            CancellationToken.None);

    internal static GacAttackOptimizationResult Optimize(
        GacPlannerState state,
        GacAttackOptimizationMode mode,
        GacTacticalOptimizationContext tacticalContext,
        GacPersonalLearningContext personalContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(tacticalContext);
        ArgumentNullException.ThrowIfNull(personalContext);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported optimization mode.");
        }

        cancellationToken.ThrowIfCancellationRequested();
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

        cancellationToken.ThrowIfCancellationRequested();
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

        cancellationToken.ThrowIfCancellationRequested();
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
        GacCounterDefenseAnalysis[] counterAnalyses =
        [
            .. choices
                .Select(ToCounterDefenseAnalysis)
                .OrderBy(analysis => analysis.Zone, StringComparer.OrdinalIgnoreCase)
                .ThenBy(analysis => analysis.DefenseName, StringComparer.OrdinalIgnoreCase)
        ];

        var search = new SearchState();
        Search(
            choices,
            index: 0,
            selected: [],
            usedUnits: [],
            totalScore: 0m,
            totalCost: 0m,
            search,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
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
            search.SearchLimitReached,
            counterAnalyses);
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
}
