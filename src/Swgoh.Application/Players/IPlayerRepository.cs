using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

public interface IPlayerRepository
{
    Task<PlayerProfile?> FindByAllyCodeAsync(long allyCode, CancellationToken cancellationToken = default);

    Task UpsertAsync(PlayerProfile player, CancellationToken cancellationToken = default);
}
