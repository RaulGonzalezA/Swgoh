using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

public interface IPlayerRepository
{
    Task<PlayerProfile?> FindByAllyCodeAsync(long allyCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PlayerProfile>> FindByGuildIdAsync(
        string guildId,
        int maxMembers = 50,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(PlayerProfile player, CancellationToken cancellationToken = default);
}
