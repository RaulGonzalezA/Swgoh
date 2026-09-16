using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RepositoryMongoDb.DependencyInjection;

using Swgoh.Application.TerritoryBattles;
using Swgoh.Infrastructure.Comlink;
using Swgoh.Infrastructure.Persistence.Documents;
using Swgoh.Infrastructure.TerritoryBattles;

namespace Swgoh.Infrastructure;

public static class RiseOfEmpireGuildDependencyInjection
{
    public static IServiceCollection AddRiseOfEmpireGuildInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMongoRepository<RiseOfEmpireGuildSyncJobDocument, string>(
            RiseOfEmpireGuildSyncJobMongoRepository.CollectionName,
            job => job.Id,
            collection => collection.CreateIfMissing());

        services.AddSingleton<IRiseOfEmpireGuildPlayerRepository, RiseOfEmpireGuildPlayerMongoRepository>();
        services.AddSingleton<IRiseOfEmpireOperationsCatalog, SwgohRiseOfEmpireOperationsCatalogClient>();
        services.AddSingleton<IRiseOfEmpireGuildSyncJobRepository, RiseOfEmpireGuildSyncJobMongoRepository>();
        services.AddSingleton<RiseOfEmpireGuildSyncQueue>();
        services.AddSingleton<IRiseOfEmpireGuildSyncQueue>(provider => provider.GetService<RiseOfEmpireGuildSyncQueue>()!);
        services.AddHostedService<RiseOfEmpireGuildSyncBackgroundService>();

        string comlinkBaseUrl = configuration["Swgoh:Comlink:BaseUrl"] ?? "http://comlink";
        services.AddHttpClient<IRiseOfEmpireGuildSource, ComlinkRiseOfEmpireGuildSource>(client =>
        {
            client.BaseAddress = new Uri(comlinkBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromMinutes(2);
        }).AddHttpMessageHandler<GacComlinkRateLimitHandler>();

        return services;
    }
}
