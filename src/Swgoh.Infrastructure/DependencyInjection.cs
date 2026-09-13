using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MongoDB.Driver;

using RepositoryMongoDb.DependencyInjection;

using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Application.Squads;
using Swgoh.Infrastructure.Comlink;
using Swgoh.Infrastructure.Gac;
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

        services.AddMongoRepository<SquadDefinitionDocument, string>(
            SquadMongoRepository.CollectionName,
            squad => squad.Id,
            collection => collection.CreateIfMissing());

        IndexKeysDefinition<GacHistoryRoundDocument> gacHistoryIndexKeys = Builders<GacHistoryRoundDocument>.IndexKeys
            .Ascending(round => round.AllyCode)
            .Ascending(round => round.Format)
            .Descending(round => round.StartedAtUtc);
        IndexKeysDefinition<GacHistoryRoundDocument> gacFormatStartedIndexKeys = Builders<GacHistoryRoundDocument>.IndexKeys
            .Ascending(round => round.Format)
            .Descending(round => round.StartedAtUtc);

        services.AddMongoRepository<GacHistoryRoundDocument, string>(
            GacHistoryMongoRepository.CollectionName,
            round => round.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    gacHistoryIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = GacHistoryMongoRepository.AllyCodeFormatStartedIndexName
                    })
                .HasIndex(
                    gacFormatStartedIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = GacHistoryMongoRepository.FormatStartedIndexName
                    }));

        string gameDataBaseUrl = configuration["Swgoh:GameData:BaseUrl"]
            ?? "https://raw.githubusercontent.com/swgoh-utils/gamedata/main/";
        string gameDataLocale = configuration["Swgoh:GameData:Locale"] ?? "SPA_XM";
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
        services.AddHttpClient(SwgohComlinkGacOpponentSource.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(comlinkBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        string? gacHistoryProviderBaseUrl = configuration["Swgoh:GacHistory:ProviderBaseUrl"];
        if (!string.IsNullOrWhiteSpace(gacHistoryProviderBaseUrl))
        {
            string? gacHistoryProviderApiKey = configuration["Swgoh:GacHistory:ApiKey"];
            services.AddHttpClient(NormalizedHttpGacHistoryProvider.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(gacHistoryProviderBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddSingleton<IGacHistoryProvider>(serviceProvider =>
                new NormalizedHttpGacHistoryProvider(
                    serviceProvider.GetRequiredService<IHttpClientFactory>(),
                    gacHistoryProviderApiKey));
        }

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPlayerRepository, PlayerMongoRepository>();
        services.AddSingleton<IPlayerSnapshotRepository, PlayerSnapshotMongoRepository>();
        services.AddSingleton<ISquadRepository, SquadMongoRepository>();
        services.AddSingleton<IGacHistoryRepository, GacHistoryMongoRepository>();
        services.AddSingleton<SwgohComlinkGacOpponentSource>();
        services.AddSingleton<ICurrentGacOpponentSource, SwgohComlinkCurrentRoundOpponentSource>();
        return services;
    }
}
