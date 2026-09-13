namespace Swgoh.Application.Gac;

public interface IGacPlannerService
{
    Task<GacPlannerLookup> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default);

    Task<GacPlannerLookup> SaveCurrentAsync(
        long allyCode,
        SaveCurrentGacRoundPlan input,
        CancellationToken cancellationToken = default);

    Task<GacTeamPresetDetails> CreatePresetAsync(
        long allyCode,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default);

    Task<GacTeamPresetDetails?> UpdatePresetAsync(
        long allyCode,
        Guid id,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default);

    Task<bool> DeletePresetAsync(
        long allyCode,
        Guid id,
        CancellationToken cancellationToken = default);
}
