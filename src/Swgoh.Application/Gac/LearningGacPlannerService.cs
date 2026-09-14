using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class LearningGacPlannerService(
    GacPlannerService inner,
    IGacPersonalLearningService personalLearningService,
    IGacPersonalBattleRepository personalBattleRepository) : IGacPlannerService
{
    public async Task<GacPlannerLookup> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        GacPlannerLookup lookup = await inner.GetCurrentAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return await EnrichBannersAsync(lookup, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GacPlannerLookup> SaveCurrentAsync(
        long allyCode,
        SaveCurrentGacRoundPlan input,
        CancellationToken cancellationToken = default)
    {
        GacPlannerLookup lookup = await inner
            .SaveCurrentAsync(allyCode, input, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.State is not null)
        {
            await personalLearningService
                .SyncPlannerStateAsync(lookup.State, cancellationToken)
                .ConfigureAwait(false);
        }

        return await EnrichBannersAsync(lookup, cancellationToken).ConfigureAwait(false);
    }

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

    private async Task<GacPlannerLookup> EnrichBannersAsync(
        GacPlannerLookup lookup,
        CancellationToken cancellationToken)
    {
        if (lookup.State is null || lookup.State.Plan.Attacks.Count == 0)
        {
            return lookup;
        }

        IReadOnlyCollection<GacPersonalBattleObservation> observations = await personalBattleRepository
            .GetRoundAsync(
                lookup.State.Plan.PlayerAllyCode,
                lookup.State.Plan.EventInstanceId,
                lookup.State.Plan.RoundNumber,
                cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, int?> bannersByAttackId = observations
            .GroupBy(observation => observation.AttackId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.RecordedAtUtc).First().Banners);
        GacAttackAssignmentDetails[] attacks =
        [
            .. lookup.State.Plan.Attacks.Select(attack => attack with
            {
                Banners = bannersByAttackId.GetValueOrDefault(attack.Id)
            })
        ];
        GacRoundPlanDetails plan = lookup.State.Plan with { Attacks = attacks };
        return lookup with { State = lookup.State with { Plan = plan } };
    }
}
