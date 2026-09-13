using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Players;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class PlayerSnapshotMongoRepository(IMongoDbRepository<PlayerSnapshotDocument, string> repository)
    : IPlayerSnapshotRepository
{
    internal const string CollectionName = "playerSnapshots";
    internal const string AllyCodeCapturedAtIndexName = "ix_player_snapshots_ally_code_captured_at";

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        PlayerSnapshotDocument? document = await repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return document is not null;
    }

    public Task UpsertAsync(PlayerSnapshot snapshot, CancellationToken cancellationToken = default) =>
        repository.UpsertAsync(ToDocument(snapshot), cancellationToken);

    public async Task<IReadOnlyCollection<PlayerSnapshot>> GetRecentAsync(
        long allyCode,
        int limit,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<PlayerSnapshotDocument> filter = Builders<PlayerSnapshotDocument>.Filter
            .Eq(document => document.AllyCode, allyCode);
        SortDefinition<PlayerSnapshotDocument> sort = Builders<PlayerSnapshotDocument>.Sort
            .Descending(document => document.CapturedAtUtc);

        IReadOnlyCollection<PlayerSnapshotDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit, sort, cancellationToken)
            .ConfigureAwait(false);

        return [.. documents.Select(ToDomain)];
    }

    private static PlayerSnapshotDocument ToDocument(PlayerSnapshot snapshot) => new()
    {
        Id = snapshot.Id,
        AllyCode = snapshot.AllyCode,
        CapturedAtUtc = snapshot.CapturedAtUtc,
        GalacticPower = snapshot.GalacticPower,
        CharacterGalacticPower = snapshot.CharacterGalacticPower,
        ShipGalacticPower = snapshot.ShipGalacticPower,
        CharacterCount = snapshot.CharacterCount,
        ShipCount = snapshot.ShipCount,
        RelicCharacters = snapshot.RelicCharacters,
        Relic7Plus = snapshot.Relic7Plus,
        Relic8Plus = snapshot.Relic8Plus,
        Relic9Plus = snapshot.Relic9Plus,
        Relic10 = snapshot.Relic10,
        Zetas = snapshot.Zetas,
        Omicrons = snapshot.Omicrons
    };

    private static PlayerSnapshot ToDomain(PlayerSnapshotDocument document) => new(
        document.Id,
        document.AllyCode,
        document.CapturedAtUtc,
        document.GalacticPower,
        document.CharacterGalacticPower,
        document.ShipGalacticPower,
        document.CharacterCount,
        document.ShipCount,
        document.RelicCharacters,
        document.Relic7Plus,
        document.Relic8Plus,
        document.Relic9Plus,
        document.Relic10,
        document.Zetas,
        document.Omicrons);
}
