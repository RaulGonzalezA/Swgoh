using Swgoh.Application.Abstractions;

namespace Swgoh.Application.Investments;

public interface IPlayerInventoryService
{
    Task<PlayerInventorySnapshot?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default);

    Task<PlayerInventorySnapshot> ImportAsync(
        long allyCode,
        PlayerInventoryImport inventory,
        CancellationToken cancellationToken = default);
}

internal sealed class PlayerInventoryService(
    IPlayerInventoryRepository repository,
    IClock clock) : IPlayerInventoryService
{
    public Task<PlayerInventorySnapshot?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        ValidateAllyCode(allyCode);
        return repository.GetAsync(allyCode, cancellationToken);
    }

    public async Task<PlayerInventorySnapshot> ImportAsync(
        long allyCode,
        PlayerInventoryImport inventory,
        CancellationToken cancellationToken = default)
    {
        ValidateAllyCode(allyCode);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(inventory.Resources);

        var resources = new Dictionary<string, PlayerInventoryResource>(StringComparer.Ordinal);
        foreach (PlayerInventoryResource resource in inventory.Resources)
        {
            if (resource.Quantity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inventory), "Inventory quantities cannot be negative.");
            }

            string candidate = string.IsNullOrWhiteSpace(resource.Id) ? resource.Name : resource.Id;
            if (!PlayerInventoryCatalog.TryNormalize(candidate, out InventoryResourceDefinition definition)
                && !PlayerInventoryCatalog.TryNormalize(resource.Name, out definition))
            {
                continue;
            }

            resources[definition.Id] = new PlayerInventoryResource(
                definition.Id,
                definition.Name,
                resource.Quantity);
        }

        foreach (InventoryResourceDefinition definition in PlayerInventoryCatalog.Resources)
        {
            resources.TryAdd(
                definition.Id,
                new PlayerInventoryResource(definition.Id, definition.Name, 0));
        }

        string source = string.IsNullOrWhiteSpace(inventory.Source)
            ? "manual"
            : inventory.Source.Trim();
        if (source.Length > 80)
        {
            throw new ArgumentException("Inventory source cannot exceed 80 characters.", nameof(inventory));
        }

        PlayerInventorySnapshot snapshot = new(
            allyCode,
            inventory.CapturedAtUtc ?? clock.UtcNow,
            source,
            [.. resources.Values.OrderBy(resource =>
                PlayerInventoryCatalog.Resources.First(definition => definition.Id == resource.Id).SortOrder)]);

        await repository.UpsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    private static void ValidateAllyCode(long allyCode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(allyCode, 100_000_000L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(allyCode, 999_999_999L);
    }
}
