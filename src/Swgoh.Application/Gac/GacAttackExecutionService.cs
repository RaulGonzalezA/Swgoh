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
    private static readonly TimeSpan PostCommitOperationTimeout = TimeSpan.FromSeconds(10);

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
        DateTimeOffset recordedAtUtc = clock.UtcNow;
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
        plan.Replace(plan.OwnDefenses, plan.VisibleDefenses, updatedAttacks, recordedAtUtc);
        bool saved = await planRepository
            .TrySaveAsync(plan, state.Plan.Version, cancellationToken)
            .ConfigureAwait(false);
        if (!saved)
        {
            throw new GacPlannerConcurrencyException(
                "The GAC plan changed while the attack result was being recorded. Reload the round and try again.");
        }

        GacPlannerState committedState = BuildCommittedState(
            state,
            attackDetails,
            input,
            notes,
            recordedAtUtc);
        var warnings = new List<string>();

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
            recordedAtUtc);
        await PersistObservationBestEffortAsync(observation, warnings).ConfigureAwait(false);

        (GacPlannerState resultState, GacAttackOptimizationResult? optimization, GacAttackOptimizationRecommendation? next) =
            await ReoptimizeBestEffortAsync(allyCode, committedState, warnings).ConfigureAwait(false);

        return new GacAttackExecutionLookup(
            CurrentGacOpponentStatus.Found,
            null,
            new GacAttackExecutionResult(
                attackId,
                input.Status,
                input.Banners,
                notes,
                resultState,
                optimization,
                next,
                warnings));
    }

    private async Task PersistObservationBestEffortAsync(
        GacPersonalBattleObservation observation,
        ICollection<string> warnings)
    {
        using var timeout = new CancellationTokenSource(PostCommitOperationTimeout);
        try
        {
            await personalBattleRepository.UpsertAsync(observation, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            warnings.Add(
                "El resultado del ataque se guardó, pero el aprendizaje personal no pudo actualizarse en esta operación.");
        }
    }

    private async Task<(GacPlannerState State, GacAttackOptimizationResult? Optimization, GacAttackOptimizationRecommendation? Next)>
        ReoptimizeBestEffortAsync(
            long allyCode,
            GacPlannerState committedState,
            ICollection<string> warnings)
    {
        using var timeout = new CancellationTokenSource(PostCommitOperationTimeout);
        try
        {
            GacAttackOptimizationLookup optimizationLookup = await optimizerService
                .OptimizeCurrentAsync(
                    allyCode,
                    GacAttackOptimizationMode.FillGaps,
                    apply: false,
                    timeout.Token)
                .ConfigureAwait(false);
            GacPlannerState resultState = optimizationLookup.State ?? committedState;
            GacAttackOptimizationRecommendation? next = optimizationLookup.Optimization?.Recommendations
                .OrderByDescending(recommendation => recommendation.Score)
                .ThenBy(recommendation => recommendation.StrategicCost)
                .FirstOrDefault();
            return (resultState, optimizationLookup.Optimization, next);
        }
        catch (Exception)
        {
            warnings.Add(
                "El resultado del ataque se guardó, pero no se pudo recalcular el siguiente ataque en esta operación.");
            return (committedState, null, null);
        }
    }

    private static GacPlannerState BuildCommittedState(
        GacPlannerState state,
        GacAttackAssignmentDetails attackDetails,
        ExecuteGacAttackResult input,
        string? notes,
        DateTimeOffset recordedAtUtc)
    {
        GacAttackAssignmentDetails committedAttack = attackDetails with
        {
            Status = input.Status,
            Notes = notes,
            Banners = input.Banners
        };
        GacAttackAssignmentDetails[] attacks =
        [
            .. state.Plan.Attacks.Select(item => item.Id == committedAttack.Id ? committedAttack : item)
        ];
        GacVisibleDefenseDetails[] visibleDefenses =
        [
            .. state.Plan.VisibleDefenses.Select(defense => defense with
            {
                Defeated = attacks.Any(attack =>
                    attack.DefenseId == defense.Id &&
                    attack.Status == GacAttackPlanStatus.Won)
            })
        ];
        GacRoundPlanDetails plan = state.Plan with
        {
            Attacks = attacks,
            VisibleDefenses = visibleDefenses,
            UpdatedAtUtc = recordedAtUtc,
            Version = state.Plan.Version + 1
        };
        return state with { Plan = plan };
    }
}
