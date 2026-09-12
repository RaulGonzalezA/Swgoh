using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

public interface IPlayerProfileService
{
    Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default);

    Task<PlayerProfile> SaveAsync(long allyCode, string name, long galacticPower, CancellationToken cancellationToken = default);

    Task<PlayerProfile> RefreshFromGameAsync(long allyCode, CancellationToken cancellationToken = default);
}
