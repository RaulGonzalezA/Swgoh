using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Swgoh.Application.TerritoryBattles;
using Swgoh.Infrastructure.Gac;

namespace Swgoh.Infrastructure;

public static class RiseOfEmpireGuildDependencyInjection
{
    public static IServiceCollection AddRiseOfEmpireGuildInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IRiseOfEmpireOperationsCatalog, TerritoryBattles.SwgohRiseOfEmpireOperationsCatalogClient>();

        string comlinkBaseUrl = configuration["Swgoh:Comlink:BaseUrl"] ?? "http://comlink";
        services.AddHttpClient<IRiseOfEmpireGuildSource, TerritoryBattles.ComlinkRiseOfEmpireGuildSource>(client =>
        {
            client.BaseAddress = new Uri(comlinkBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromMinutes(2);
        }).AddHttpMessageHandler<GacComlinkRateLimitHandler>();

        return services;
    }
}
