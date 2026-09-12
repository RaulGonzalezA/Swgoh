using RepositoryMongoDb.Repository;

using Swgoh.Application.Players;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class PlayerSnapshotMongoRepository(IMongoDbRepository<PlayerSnapshotDocument, string> repository)
    : IPlayerSnapshotRepository
{
    internal const string CollectionName = "playerSnapshots";

    public Task UpsertAsync(PlayerSnapshot snapshot, CancellationToken cancellationToken = default) =>
        repository.UpsertAsync(ToDocument(snapshot), cancellationToken);

    public async Task<IReadOnlyCollection<PlayerSnapshot>> GetRecentAsync(
        long allyCode,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<PlayerSnapshotDocument> documents = await repository.FindAllAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. documents
                .Where(document => document.AllyCode == allyCode)
                .OrderByDescending(document => document.CapturedAtUtc)
                .Take(limit)
                .Select(ToDomain)
        ];
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
        document.Zetas,
        document.Omicrons);
}
