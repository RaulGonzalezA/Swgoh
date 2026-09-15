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
            preview.Optimization is null ||
            preview.Optimization.Recommendations.Count == 0)
        {
            return preview;
        }

        cancellationToken.ThrowIfCancellationRequested();
        GacPlannerState state = preview.State;
        GacAttackOptimizationResult optimization = preview.Optimization;
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
