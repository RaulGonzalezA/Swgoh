using Swgoh.Application.Abstractions;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

internal sealed class PlayerProfileService(IPlayerRepository repository, IClock clock) : IPlayerProfileService
{
    public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
        repository.FindByAllyCodeAsync(allyCode, cancellationToken);

    public async Task<PlayerProfile> SaveAsync(long allyCode, string name, long galacticPower, CancellationToken cancellationToken = default)
    {
        PlayerProfile? player = await repository.FindByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);

        if (player is null)
        {
            player = PlayerProfile.Create(allyCode, name, galacticPower, clock.UtcNow);
        }
        else
        {
            player.Refresh(name, galacticPower, clock.UtcNow);
        }

        await repository.UpsertAsync(player, cancellationToken).ConfigureAwait(false);
        return player;
    }
}
