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

    Task UpsertAsync(GacRoundPlan plan, CancellationToken cancellationToken = default);
}
