using RepositoryMongoDb.Repository;

using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class PlayerMongoRepository(IMongoDbRepository<PlayerProfile, long> repository) : IPlayerRepository
{
    internal const string CollectionName = "players";

    public Task<PlayerProfile?> FindByAllyCodeAsync(long allyCode, CancellationToken cancellationToken = default) =>
        repository.FindByIdAsync(allyCode, cancellationToken);

    public async Task UpsertAsync(PlayerProfile player, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        await repository.UpsertAsync(player, cancellationToken).ConfigureAwait(false);
    }
}
