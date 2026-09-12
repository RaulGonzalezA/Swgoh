using Microsoft.Extensions.DependencyInjection;

using Swgoh.Application.Players;

namespace Swgoh.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<PlayerRefreshLock>();
        services.AddScoped<IPlayerProfileService, PlayerProfileService>();
        services.AddScoped<IPlayerAnalysisService, PlayerAnalysisService>();
        services.AddScoped<IPlayerHistoryService, PlayerHistoryService>();
        services.AddScoped<IGalacticLegendProgressService, GalacticLegendProgressService>();
        return services;
    }
}
