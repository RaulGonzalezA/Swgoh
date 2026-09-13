using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MongoDB.Driver;

using RepositoryMongoDb.DependencyInjection;

using Swgoh.Application.Abstractions;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Infrastructure.Comlink;
using Swgoh.Infrastructure.GameData;
using Swgoh.Infrastructure.Persistence;
using Swgoh.Infrastructure.Persistence.Documents;
using Swgoh.Infrastructure.Time;

namespace Swgoh.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("swgoh")
            ?? throw new InvalidOperationException("Connection string 'swgoh' is required.");

        services.AddMongoDb(settings =>
        {
            settings.ConnectionString = connectionString;
            settings.DatabaseName = "swgoh";
        });

        services.AddMongoRepository<PlayerDocument, long>(
            PlayerMongoRepository.CollectionName,
            player => player.AllyCode,
            collection => collection.CreateIfMissing());

        IndexKeysDefinition<PlayerSnapshotDocument> snapshotIndexKeys = Builders<PlayerSnapshotDocument>.IndexKeys
            .Ascending(snapshot => snapshot.AllyCode)
            .Descending(snapshot => snapshot.CapturedAtUtc);

        services.AddMongoRepository<PlayerSnapshotDocument, string>(
            PlayerSnapshotMongoRepository.CollectionName,
            snapshot => snapshot.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    snapshotIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = PlayerSnapshotMongoRepository.AllyCodeCapturedAtIndexName
                    }));

        string gameDataBaseUrl = configuration["Swgoh:GameData:BaseUrl"]
            ?? "https://raw.githubusercontent.com/swgoh-utils/gamedata/main/";
        string gameDataLocale = configuration["Swgoh:GameData:Locale"] ?? "ENG_US";
        services.AddHttpClient(SwgohGameDataCatalogClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(gameDataBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton<ISwgohGameDataCatalog>(serviceProvider =>
            new SwgohGameDataCatalogClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                gameDataLocale));

        string statsBaseUrl = configuration["Swgoh:Stats:BaseUrl"] ?? "http://swgoh-stats:3223";
        services.AddHttpClient<ISwgohStatsClient, SwgohStatsClient>(client =>
        {
            client.BaseAddress = new Uri(statsBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        string comlinkBaseUrl = configuration["Swgoh:Comlink:BaseUrl"] ?? "http://comlink";
        services.AddHttpClient<ISwgohPlayerClient, SwgohComlinkClient>(client =>
        {
            client.BaseAddress = new Uri(comlinkBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPlayerRepository, PlayerMongoRepository>();
        services.AddSingleton<IPlayerSnapshotRepository, PlayerSnapshotMongoRepository>();
        return services;
    }
}
