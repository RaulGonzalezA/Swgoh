using Microsoft.Extensions.DependencyInjection;

using Swgoh.Application.Conquest;
using Swgoh.Application.Gac;
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
        services.AddScoped<IConquestService, ConquestService>();
        services.AddScoped<IConquestDailyPlanService, ConquestDailyPlanService>();
        services.AddSingleton<IGacRulesService, GacRulesService>();
        services.AddScoped<IGacHistoryService, GacHistoryService>();
        services.AddScoped<IGacHistorySyncService, GacHistorySyncService>();
        services.AddScoped<IGacCounterStatisticsService, GacCounterStatisticsService>();
        services.AddScoped<IOpponentScoutingService, OpponentScoutingService>();
        services.AddScoped<ICurrentGacScoutingService, CurrentGacScoutingService>();
        services.AddScoped<IGacPersonalLearningService, GacPersonalLearningService>();
        services.AddScoped<GacPlannerService>();
        services.AddScoped<IGacPlannerService, LearningGacPlannerService>();
        services.AddScoped<IGacAttackPlanOptimizerService, GacAttackPlanOptimizerService>();
        services.AddScoped<IGacAttackExecutionService, GacAttackExecutionService>();
        return services;
    }
}
