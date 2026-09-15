using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacTeamPresetRepository
{
    Task<GacTeamPreset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<GacTeamPreset>> GetAsync(
        long allyCode,
        GacFormat? format,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(GacTeamPreset preset, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IGacRoundPlanRepository
{
    Task<GacRoundPlan?> FindByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<long> GetVersionAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(0L);

    Task<bool> TrySaveAsync(
        GacRoundPlan plan,
        long expectedVersion,
        CancellationToken cancellationToken = default) =>
        SaveFallbackAsync(plan, cancellationToken);

    Task UpsertAsync(GacRoundPlan plan, CancellationToken cancellationToken = default);

    private async Task<bool> SaveFallbackAsync(GacRoundPlan plan, CancellationToken cancellationToken)
    {
        await UpsertAsync(plan, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
