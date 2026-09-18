using Swgoh.Application.Abstractions;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class HardenedGacAttackPlanOptimizerService(
    GacAttackPlanOptimizerService inner,
    IGacPlannerService plannerService,
    IGacRoundPlanRepository planRepository,
    IGacGeneratedTeamLifecycleService generatedTeamLifecycleService,
    GacOptimizationCoordinator optimizationCoordinator,
    IClock clock) : IGacAttackPlanOptimizerService
{
    public async Task<GacAttackOptimizationLookup> OptimizeCurrentAsync(
        long allyCode,
        GacAttackOptimizationMode mode,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        using IDisposable lease = await optimizationCoordinator
            .AcquireAsync(cancellationToken)
            .ConfigureAwait(false);

        GacAttackOptimizationLookup preview = await inner
            .OptimizeCurrentAsync(allyCode, mode, apply: false, cancellationToken)
            .ConfigureAwait(false);
        if (!apply ||
            preview.State is null ||
            preview.Optimization is null)
        {
            return preview;
        }

        cancellationToken.ThrowIfCancellationRequested();
        GacPlannerState state = preview.State;
        GacAttackOptimizationResult optimization = preview.Optimization;
        if (optimization.Recommendations.Count == 0)
        {
            return mode == GacAttackOptimizationMode.RebuildPlanned
                ? await ApplyEmptyRebuildAsync(
                    allyCode,
                    state,
                    optimization,
                    cancellationToken).ConfigureAwait(false)
                : preview;
        }
        IReadOnlyDictionary<Guid, string?> temporaryDatacrons = AllocateAttackDatacrons(
            state,
            optimization.Recommendations,
            mode);
        GacAttackPresetMaterialization materialization = await GacAttackGeneratedPresetMaterializer
            .MaterializeAsync(
                plannerService,
                allyCode,
                state.Plan.Format,
                optimization,
                cancellationToken)
            .ConfigureAwait(false);
        string generationId = Guid.NewGuid().ToString("N");
        optimization = GacAttackGeneratedPresetMaterializer.Remap(optimization, materialization.IdMap);
        Dictionary<Guid, string?> appliedDatacrons = temporaryDatacrons.ToDictionary(
            item => materialization.IdMap.GetValueOrDefault(item.Key, item.Key),
            item => item.Value);

        try
        {
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
                string? datacronId = appliedDatacrons.GetValueOrDefault(recommendation.TeamPresetId);
                string datacronNote = datacronId is null
                    ? recommendation.DatacronStatus
                    : $"{datacronId} ({recommendation.DatacronStatus})";
                string notes = $"Counter Engine 2.0: {recommendation.Evidence}; score {recommendation.Score:0.#}; " +
                    $"win estimado {recommendation.EstimatedWinProbability:0.#}%; riesgo {recommendation.Risk}; " +
                    $"timeout {recommendation.TimeoutRisk}; coste {recommendation.StrategicCost:0.#} " +
                    $"(piezas críticas {recommendation.CriticalPieceCost:0.#}); " +
                    $"ajuste táctico {recommendation.TacticalAdjustment:+0.#;-0.#;0};{personalNote} " +
                    $"datacron {datacronNote}.";
                retainedAttacks.Add(GacAttackAssignment.Create(
                    Guid.NewGuid(),
                    recommendation.DefenseId,
                    recommendation.TeamPresetId,
                    attempt,
                    GacAttackPlanStatus.Planned,
                    notes,
                    datacronId));
            }

            plan.Replace(plan.OwnDefenses, plan.VisibleDefenses, retainedAttacks, clock.UtcNow);
            bool saved = await planRepository
                .TrySaveAsync(plan, state.Plan.Version, cancellationToken)
                .ConfigureAwait(false);
            if (!saved)
            {
                throw new GacPlannerConcurrencyException(
                    "The GAC plan changed while the attack optimizer was running. Recalculate before applying the result.");
            }
        }
        catch
        {
            await GacRosterDefenseCandidateService
                .RollbackMaterializationAsync(plannerService, allyCode, materialization.CreatedPresetIds)
                .ConfigureAwait(false);
            throw;
        }

        await RegisterLifecycleBestEffortAsync(
            allyCode,
            state,
            generationId,
            materialization.CreatedPresetIds,
            cancellationToken).ConfigureAwait(false);

        GacPlannerLookup refreshed = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (refreshed.State is not null)
        {
            await PruneBestEffortAsync(allyCode, refreshed.State, cancellationToken).ConfigureAwait(false);
        }

        return new GacAttackOptimizationLookup(
            refreshed.Status,
            refreshed.Message,
            refreshed.State,
            optimization with { Applied = true });
    }

    private async Task<GacAttackOptimizationLookup> ApplyEmptyRebuildAsync(
        long allyCode,
        GacPlannerState state,
        GacAttackOptimizationResult optimization,
        CancellationToken cancellationToken)
    {
        GacRoundPlan plan = await planRepository
            .FindByIdAsync(state.Plan.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The current GAC round plan could not be loaded for optimization.");

        GacAttackAssignment[] retainedAttacks =
        [
            .. plan.Attacks.Where(attack => attack.Status != GacAttackPlanStatus.Planned)
        ];
        if (retainedAttacks.Length != plan.Attacks.Count)
        {
            plan.Replace(plan.OwnDefenses, plan.VisibleDefenses, retainedAttacks, clock.UtcNow);
            bool saved = await planRepository
                .TrySaveAsync(plan, state.Plan.Version, cancellationToken)
                .ConfigureAwait(false);
            if (!saved)
            {
                throw new GacPlannerConcurrencyException(
                    "The GAC plan changed while the attack optimizer was running. Recalculate before applying the result.");
            }
        }

        GacPlannerLookup refreshed = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (refreshed.State is not null)
        {
            await PruneBestEffortAsync(allyCode, refreshed.State, cancellationToken).ConfigureAwait(false);
        }

        return new GacAttackOptimizationLookup(
            refreshed.Status,
            refreshed.Message,
            refreshed.State,
            optimization with { Applied = true });
    }

    internal static IReadOnlyDictionary<Guid, string?> AllocateAttackDatacrons(
        GacPlannerState state,
        IReadOnlyCollection<GacAttackOptimizationRecommendation> recommendations,
        GacAttackOptimizationMode mode)
    {
        Dictionary<Guid, GacTeamPresetDetails> teams = state.Presets.ToDictionary(item => item.Id);
        var used = state.Plan.OwnDefenses
            .Select(defense => defense.DatacronId)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string id in state.Plan.Attacks
                     .Where(attack => attack.Status is GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed)
                     .Select(attack => attack.DatacronId)
                     .Where(id => id is not null)
                     .Select(id => id!))
        {
            used.Add(id);
        }

        if (mode == GacAttackOptimizationMode.FillGaps)
        {
            foreach (string id in state.Plan.Attacks
                         .Where(attack => attack.Status == GacAttackPlanStatus.Planned)
                         .Select(attack => attack.DatacronId)
                         .Where(id => id is not null)
                         .Select(id => id!))
            {
                used.Add(id);
            }
        }

        var result = new Dictionary<Guid, string?>();
        foreach (GacAttackOptimizationRecommendation recommendation in recommendations
                     .OrderByDescending(item => item.EstimatedWinProbability)
                     .ThenByDescending(item => item.Score))
        {
            if (!teams.TryGetValue(recommendation.TeamPresetId, out GacTeamPresetDetails? team) || team.Squad.IsFleet)
            {
                result[recommendation.TeamPresetId] = null;
                continue;
            }

            GacPlannerDatacronDetails? datacron = GacDatacronRules.BestEligible(
                team,
                state.PlayerDatacrons,
                used);
            result[recommendation.TeamPresetId] = datacron?.Id;
            if (datacron is not null)
            {
                used.Add(datacron.Id);
            }
        }

        return result;
    }

    private async Task RegisterLifecycleBestEffortAsync(
        long allyCode,
        GacPlannerState state,
        string generationId,
        IReadOnlyCollection<Guid> createdPresetIds,
        CancellationToken cancellationToken)
    {
        try
        {
            await generatedTeamLifecycleService.RegisterAsync(
                allyCode,
                state.Plan.Format,
                GacGeneratedTeamOrigin.CounterEngine,
                generationId,
                state.Plan.Id,
                createdPresetIds,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The plan is already committed. Legacy generated-team detection keeps these teams isolated.
        }
    }

    private async Task PruneBestEffortAsync(
        long allyCode,
        GacPlannerState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await generatedTeamLifecycleService
                .PruneUnreferencedAsync(allyCode, state, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cleanup is post-commit maintenance and must not turn a valid applied plan into a failure.
        }
    }
}
