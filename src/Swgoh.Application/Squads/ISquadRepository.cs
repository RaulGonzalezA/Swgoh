using Swgoh.Domain.Squads;

namespace Swgoh.Application.Squads;

public interface ISquadRepository
{
    Task<SquadDefinition?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SquadDefinition>> SearchAsync(
        SquadSearchQuery query,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(SquadDefinition squad, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
