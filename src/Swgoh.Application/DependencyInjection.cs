using Microsoft.Extensions.DependencyInjection;

using Swgoh.Application.Players;
using Swgoh.Application.Squads;

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
        services.AddScoped<IPlayerRosterService, PlayerRosterService>();
        services.AddScoped<IGalacticLegendProgressService, GalacticLegendProgressService>();
        services.AddScoped<ISquadService, SquadService>();
        return services;
    }
}
