using Swgoh.Application.Abstractions;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacAttackExecutionService
{
    Task<GacAttackExecutionLookup> ExecuteAsync(
        long allyCode,
        Guid attackId,
        ExecuteGacAttackResult input,
        CancellationToken cancellationToken = default);
}

internal sealed class GacAttackExecutionService(
    IGacPlannerService plannerService,
    IGacRoundPlanRepository planRepository,
    IGacPersonalBattleRepository personalBattleRepository,
    IGacAttackPlanOptimizerService optimizerService,
    IClock clock) : IGacAttackExecutionService
{
    public async Task<GacAttackExecutionLookup> ExecuteAsync(
        long allyCode,
        Guid attackId,
        ExecuteGacAttackResult input,
        CancellationToken cancellationToken = default)
    {
        if (attackId == Guid.Empty)
        {
            throw new ArgumentException("Attack ID cannot be empty.", nameof(attackId));
        }

        ArgumentNullException.ThrowIfNull(input);
        if (input.Status is not (GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed))
        {
            throw new ArgumentException("Execution result must be Won or Failed.", nameof(input));
        }

        if (input.Banners is int banners)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(banners, nameof(input));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(banners, 100, nameof(input));
        }

        GacPlannerLookup plannerLookup = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (!plannerLookup.IsAvailable || plannerLookup.State is null)
        {
            return new GacAttackExecutionLookup(plannerLookup.Status, plannerLookup.Message, null);
        }

        GacPlannerState state = plannerLookup.State;
        GacAttackAssignmentDetails attackDetails = state.Plan.Attacks.FirstOrDefault(attack => attack.Id == attackId)
            ?? throw new ArgumentException("Attack is not part of the current GAC round.", nameof(attackId));
        if (attackDetails.Status == GacAttackPlanStatus.Cancelled)
        {
            throw new ArgumentException("A cancelled attack cannot receive a result.", nameof(attackId));
        }

        GacVisibleDefenseDetails defense = state.Plan.VisibleDefenses.FirstOrDefault(item => item.Id == attackDetails.DefenseId)
            ?? throw new InvalidOperationException("The attack references a defense that is no longer visible in the plan.");
        GacRoundPlan plan = await planRepository
            .FindByIdAsync(state.Plan.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The current GAC round plan could not be loaded for execution.");
        GacAttackAssignment attack = plan.Attacks.FirstOrDefault(item => item.Id == attackId)
            ?? throw new InvalidOperationException("The current GAC round plan no longer contains the selected attack.");

        string? notes = string.IsNullOrWhiteSpace(input.Notes) ? attack.Notes : input.Notes.Trim();
        GacAttackAssignment updatedAttack = GacAttackAssignment.Create(
            attack.Id,
            attack.DefenseId,
            attack.TeamPresetId,
            attack.Attempt,
            input.Status,
            notes);
        GacAttackAssignment[] updatedAttacks =
        [
            .. plan.Attacks.Select(item => item.Id == attackId ? updatedAttack : item)
        ];
        plan.Replace(plan.OwnDefenses, plan.VisibleDefenses, updatedAttacks, clock.UtcNow);
        bool saved = await planRepository
            .TrySaveAsync(plan, state.Plan.Version, cancellationToken)
            .ConfigureAwait(false);
        if (!saved)
        {
            throw new GacPlannerConcurrencyException(
                "The GAC plan changed while the attack result was being recorded. Reload the round and try again.");
        }

        GacPersonalBattleObservation observation = GacPersonalBattleObservation.Create(
            state.Plan.PlayerAllyCode,
            state.Plan.OpponentAllyCode,
            state.Plan.EventInstanceId,
            state.Plan.RoundNumber,
            state.Plan.Format,
            attack.Id,
            attack.DefenseId,
            attack.Attempt,
            defense.Squad.IsFleet,
            attackDetails.Team.Squad.AllUnits.Select(unit => unit.DefinitionId),
            defense.Squad.AllUnits.Select(unit => unit.DefinitionId),
            input.Status == GacAttackPlanStatus.Won,
            input.Banners,
            clock.UtcNow);
        await personalBattleRepository.UpsertAsync(observation, cancellationToken).ConfigureAwait(false);

        GacAttackOptimizationLookup optimizationLookup = await optimizerService
            .OptimizeCurrentAsync(
                allyCode,
                GacAttackOptimizationMode.FillGaps,
                apply: false,
                cancellationToken)
            .ConfigureAwait(false);
        GacPlannerState refreshedState = optimizationLookup.State
            ?? throw new InvalidOperationException("The GAC planner became unavailable after recording the result.");
        GacAttackOptimizationRecommendation? next = optimizationLookup.Optimization?.Recommendations
            .OrderByDescending(recommendation => recommendation.Score)
            .ThenBy(recommendation => recommendation.StrategicCost)
            .FirstOrDefault();

        return new GacAttackExecutionLookup(
            CurrentGacOpponentStatus.Found,
            null,
            new GacAttackExecutionResult(
                attackId,
                input.Status,
                input.Banners,
                notes,
                refreshedState,
                optimizationLookup.Optimization,
                next));
    }
}
