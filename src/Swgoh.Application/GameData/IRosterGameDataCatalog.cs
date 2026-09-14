namespace Swgoh.Application.GameData;

public interface IRosterGameDataCatalog
{
    Task<IReadOnlyDictionary<string, GameUnitDefinition>> GetUnitsAsync(
        CancellationToken cancellationToken = default);
}
