namespace Swgoh.Application.Gac;

internal sealed class LearningGacPlannerService(
    GacPlannerService inner,
    IGacPersonalOutcomeRepository personalOutcomeRepository) : IGacPlannerService
{
    public Task<GacPlannerLookup> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default) =>
        inner.GetCurrentAsync(allyCode, cancellationToken);

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
            await personalOutcomeRepository
                .UpsertAsync(GacPersonalOutcomeFactory.FromState(lookup.State), cancellationToken)
                .ConfigureAwait(false);
        }

        return lookup;
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
}
