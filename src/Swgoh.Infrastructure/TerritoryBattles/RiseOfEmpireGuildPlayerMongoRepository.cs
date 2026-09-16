using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Players;
using Swgoh.Application.TerritoryBattles;
using Swgoh.Domain.Players;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.TerritoryBattles;

internal sealed class RiseOfEmpireGuildPlayerMongoRepository(
    IMongoDbRepository<PlayerDocument, long> documents,
    IPlayerRepository players) : IRiseOfEmpireGuildPlayerRepository
{
    public async Task<IReadOnlyCollection<PlayerProfile>> FindByGuildIdAsync(
        string guildId,
        int maxMembers = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        int limit = Math.Clamp(maxMembers, 1, 50);
        FilterDefinition<PlayerDocument> filter = Builders<PlayerDocument>.Filter
            .Eq(document => document.GuildId, guildId.Trim());
        SortDefinition<PlayerDocument> sort = Builders<PlayerDocument>.Sort
            .Descending(document => document.GalacticPower)
            .Ascending(document => document.Name);
        List<PlayerDocument> matches = await documents
            .FindPageAsync(filter, skip: 0, limit, sort, cancellationToken)
            .ConfigureAwait(false);

        PlayerProfile?[] profiles = await Task.WhenAll(matches.Select(document =>
            players.FindByAllyCodeAsync(document.AllyCode, cancellationToken))).ConfigureAwait(false);
        return [.. profiles.Where(profile => profile is not null).Select(profile => profile!)];
    }
}
