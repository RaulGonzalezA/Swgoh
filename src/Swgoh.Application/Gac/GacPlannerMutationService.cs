using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacPlannerMutationService
{
    Task<GacPlannerLookup> AddOwnDefenseAsync(
        long allyCode,
        string zone,
        Guid teamPresetId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<GacPlannerLookup> RemoveOwnDefenseAsync(
        long allyCode,
        Guid assignmentId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<GacPlannerLookup> AddVisibleDefenseAsync(
        long allyCode,
        SaveGacVisibleDefense defense,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<GacPlannerLookup> RemoveVisibleDefenseAsync(
        long allyCode,
        Guid defenseId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<GacPlannerLookup> AddAttackAsync(
        long allyCode,
        Guid defenseId,
        Guid teamPresetId,
        string? notes,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<GacPlannerLookup> UpdateAttackAsync(
        long allyCode,
        Guid attackId,
        GacAttackPlanStatus status,
        string? notes,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class GacPlannerConcurrencyException(string message) : InvalidOperationException(message);

internal sealed class GacPlannerMutationService(IGacPlannerService plannerService) : IGacPlannerMutationService
{
    public Task<GacPlannerLookup> AddOwnDefenseAsync(
        long allyCode,
        string zone,
        Guid teamPresetId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            allyCode,
            expectedUpdatedAtUtc,
            (state, ownDefenses, visibleDefenses, attacks) =>
            {
                if (ownDefenses.Any(item => item.TeamPresetId == teamPresetId))
                {
                    throw new ArgumentException("The selected team is already assigned to defense.", nameof(teamPresetId));
                }

                ownDefenses.Add(new SaveGacOwnDefenseAssignment(null, zone, teamPresetId));
            },
            cancellationToken);

    public Task<GacPlannerLookup> RemoveOwnDefenseAsync(
        long allyCode,
        Guid assignmentId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            allyCode,
            expectedUpdatedAtUtc,
            (_, ownDefenses, _, _) => ownDefenses.RemoveAll(item => item.Id == assignmentId),
            cancellationToken);

    public Task<GacPlannerLookup> AddVisibleDefenseAsync(
        long allyCode,
        SaveGacVisibleDefense defense,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defense);
        return MutateAsync(
            allyCode,
            expectedUpdatedAtUtc,
            (_, _, visibleDefenses, _) => visibleDefenses.Add(defense with { Id = null }),
            cancellationToken);
    }

    public Task<GacPlannerLookup> RemoveVisibleDefenseAsync(
        long allyCode,
        Guid defenseId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            allyCode,
            expectedUpdatedAtUtc,
            (_, _, visibleDefenses, attacks) =>
            {
                visibleDefenses.RemoveAll(item => item.Id == defenseId);
                attacks.RemoveAll(item => item.DefenseId == defenseId);
            },
            cancellationToken);

    public Task<GacPlannerLookup> AddAttackAsync(
        long allyCode,
        Guid defenseId,
        Guid teamPresetId,
        string? notes,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            allyCode,
            expectedUpdatedAtUtc,
            (_, _, visibleDefenses, attacks) =>
            {
                if (visibleDefenses.All(item => item.Id != defenseId))
                {
                    throw new ArgumentException("The selected defense is not part of the current round plan.", nameof(defenseId));
                }

                int attempt = attacks
                    .Where(item => item.DefenseId == defenseId)
                    .Select(item => item.Attempt)
                    .DefaultIfEmpty(0)
                    .Max() + 1;
                attacks.Add(new SaveGacAttackAssignment(
                    null,
                    defenseId,
                    teamPresetId,
                    attempt,
                    GacAttackPlanStatus.Planned,
                    notes));
            },
            cancellationToken);

    public Task<GacPlannerLookup> UpdateAttackAsync(
        long allyCode,
        Guid attackId,
        GacAttackPlanStatus status,
        string? notes,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            allyCode,
            expectedUpdatedAtUtc,
            (_, _, _, attacks) =>
            {
                int index = attacks.FindIndex(item => item.Id == attackId);
                if (index < 0)
                {
                    throw new ArgumentException("The selected attack is not part of the current round plan.", nameof(attackId));
                }

                attacks[index] = attacks[index] with { Status = status, Notes = notes };
            },
            cancellationToken);

    private async Task<GacPlannerLookup> MutateAsync(
        long allyCode,
        DateTimeOffset? expectedUpdatedAtUtc,
        Action<
            GacPlannerState,
            List<SaveGacOwnDefenseAssignment>,
            List<SaveGacVisibleDefense>,
            List<SaveGacAttackAssignment>> mutate,
        CancellationToken cancellationToken)
    {
        GacPlannerLookup current = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (!current.IsAvailable || current.State is null)
        {
            return current;
        }

        EnsureVersion(current.State.Plan, expectedUpdatedAtUtc);

        List<SaveGacOwnDefenseAssignment> ownDefenses =
        [
            .. current.State.Plan.OwnDefenses.Select(item => new SaveGacOwnDefenseAssignment(
                item.Id,
                item.Zone,
                item.Team.Id))
        ];
        List<SaveGacVisibleDefense> visibleDefenses =
        [
            .. current.State.Plan.VisibleDefenses.Select(item => new SaveGacVisibleDefense(
                item.Id,
                item.Zone,
                item.Label,
                item.Squad.Leader.DefinitionId,
                [.. item.Squad.Members.Select(unit => unit.DefinitionId)],
                item.Squad.IsFleet))
        ];
        List<SaveGacAttackAssignment> attacks =
        [
            .. current.State.Plan.Attacks.Select(item => new SaveGacAttackAssignment(
                item.Id,
                item.DefenseId,
                item.Team.Id,
                item.Attempt,
                item.Status,
                item.Notes))
        ];

        mutate(current.State, ownDefenses, visibleDefenses, attacks);

        return await plannerService
            .SaveCurrentAsync(
                allyCode,
                new SaveCurrentGacRoundPlan(ownDefenses, visibleDefenses, attacks),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureVersion(GacRoundPlanDetails plan, DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (expectedUpdatedAtUtc is DateTimeOffset expected && plan.UpdatedAtUtc != expected)
        {
            throw new GacPlannerConcurrencyException(
                "The GAC plan changed since it was loaded. Reload the round before applying this change.");
        }
    }
}
