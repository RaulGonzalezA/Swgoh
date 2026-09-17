using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class ExecutionAwareGacPlannerService(
    LearningGacPlannerService inner,
    IGacLiveAttackStateRepository liveStateRepository) : IGacPlannerService
{
    public async Task<GacPlannerLookup> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default) =>
        await EnrichAsync(
            await inner.GetCurrentAsync(allyCode, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<GacPlannerLookup> SaveCurrentAsync(
        long allyCode,
        SaveCurrentGacRoundPlan input,
        CancellationToken cancellationToken = default) =>
        await EnrichAsync(
            await inner.SaveCurrentAsync(allyCode, input, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public Task<GacTeamPresetDetails> CreatePresetAsync(
        long allyCode,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default) =>
        inner.CreatePresetAsync(allyCode, input, cancellationToken);

    public Task<GacTeamPresetDetails?> UpdatePresetAsync(
        long allyCode,
        Guid id,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default) =>
        inner.UpdatePresetAsync(allyCode, id, input, cancellationToken);

    public Task<bool> DeletePresetAsync(
        long allyCode,
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.DeletePresetAsync(allyCode, id, cancellationToken);

    private async Task<GacPlannerLookup> EnrichAsync(
        GacPlannerLookup lookup,
        CancellationToken cancellationToken)
    {
        if (!lookup.IsAvailable || lookup.State is null)
        {
            return lookup;
        }

        IReadOnlyCollection<GacLiveAttackState> executions = await liveStateRepository
            .GetByPlanAsync(lookup.State.Plan.Id, cancellationToken)
            .ConfigureAwait(false);
        if (executions.Count == 0)
        {
            return lookup;
        }

        Dictionary<Guid, GacLiveAttackState> byAttack = executions
            .GroupBy(item => item.AttackId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.RecordedAtUtc).First());
        Dictionary<Guid, GacLiveAttackState> latestByDefense = executions
            .GroupBy(item => item.DefenseId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.Attempt)
                    .ThenByDescending(item => item.RecordedAtUtc)
                    .First());

        GacAttackAssignmentDetails[] attacks =
        [
            .. lookup.State.Plan.Attacks.Select(attack =>
                byAttack.TryGetValue(attack.Id, out GacLiveAttackState? live)
                    ? attack with
                    {
                        Banners = live.Banners,
                        RemainingEnemyUnitDefinitionIds = live.RemainingEnemyUnitDefinitionIds,
                        PreloadedTurnMeter = live.PreloadedTurnMeter
                    }
                    : attack)
        ];

        GacVisibleDefenseDetails[] visibleDefenses =
        [
            .. lookup.State.Plan.VisibleDefenses.Select(defense =>
            {
                if (!latestByDefense.TryGetValue(defense.Id, out GacLiveAttackState? live))
                {
                    return defense;
                }

                if (live.Status == GacAttackPlanStatus.Won)
                {
                    return defense with { Defeated = true };
                }

                GacPlannerSquadDetails remaining = ReduceSquad(
                    defense.Squad,
                    live.RemainingEnemyUnitDefinitionIds);
                return defense with { Squad = remaining, Defeated = false };
            })
        ];

        GacRoundPlanDetails plan = lookup.State.Plan with
        {
            Attacks = attacks,
            VisibleDefenses = visibleDefenses
        };
        return lookup with { State = lookup.State with { Plan = plan } };
    }

    private static GacPlannerSquadDetails ReduceSquad(
        GacPlannerSquadDetails squad,
        IReadOnlyCollection<string> survivorIds)
    {
        if (survivorIds.Count == 0)
        {
            return squad;
        }

        HashSet<string> survivors = survivorIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        GacPlannerUnitDetails[] remaining =
        [
            .. squad.AllUnits.Where(unit => survivors.Contains(unit.DefinitionId))
        ];
        if (remaining.Length == 0)
        {
            return squad;
        }

        return new GacPlannerSquadDetails(
            remaining[0],
            [.. remaining.Skip(1)],
            squad.IsFleet);
    }
}
