using RepositoryMongoDb.Repository;

using Swgoh.Application.Investments;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class PlayerInventoryMongoRepository(
    IMongoDbRepository<PlayerInventoryDocument, long> repository) : IPlayerInventoryRepository
{
    internal const string CollectionName = "playerInventories";

    public async Task<PlayerInventorySnapshot?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        PlayerInventoryDocument? document = await repository
            .FindByIdAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public Task UpsertAsync(
        PlayerInventorySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return repository.UpsertAsync(ToDocument(snapshot), cancellationToken);
    }

    private static PlayerInventoryDocument ToDocument(PlayerInventorySnapshot snapshot) => new()
    {
        AllyCode = snapshot.AllyCode,
        CapturedAtUtc = snapshot.CapturedAtUtc,
        Source = snapshot.Source,
        Resources =
        [
            .. snapshot.Resources.Select(resource => new PlayerInventoryResourceDocument
            {
                Id = resource.Id,
                Name = resource.Name,
                Quantity = resource.Quantity
            })
        ]
    };

    private static PlayerInventorySnapshot ToDomain(PlayerInventoryDocument document) => new(
        document.AllyCode,
        document.CapturedAtUtc,
        document.Source,
        [
            .. document.Resources.Select(resource => new PlayerInventoryResource(
                resource.Id,
                resource.Name,
                resource.Quantity))
        ]);
}
