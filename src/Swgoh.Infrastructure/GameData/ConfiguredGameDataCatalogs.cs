using Swgoh.Application.GameData;

namespace Swgoh.Infrastructure.GameData;

internal sealed class ConfiguredSwgohGameDataCatalog : ISwgohGameDataCatalog
{
    private readonly SwgohGameDataCatalogClient inner;

    public ConfiguredSwgohGameDataCatalog(
        IHttpClientFactory httpClientFactory,
        SwgohGameDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        inner = new SwgohGameDataCatalogClient(httpClientFactory, options.Locale);
    }

    public Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default) =>
        inner.GetAsync(cancellationToken);
}

internal sealed class ConfiguredSwgohRosterGameDataCatalog : IRosterGameDataCatalog
{
    private readonly SwgohRosterGameDataCatalogClient inner;

    public ConfiguredSwgohRosterGameDataCatalog(
        IHttpClientFactory httpClientFactory,
        SwgohGameDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        inner = new SwgohRosterGameDataCatalogClient(httpClientFactory, options.Locale);
    }

    public Task<IReadOnlyDictionary<string, GameUnitDefinition>> GetUnitsAsync(
        CancellationToken cancellationToken = default) =>
        inner.GetUnitsAsync(cancellationToken);
}
