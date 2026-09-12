namespace Swgoh.Application.GameData;

public interface ISwgohGameDataCatalog
{
    Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default);
}
