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
        services.AddSingleton<PlayerRosterSnapshotCache>();
        services.AddScoped<IPlayerProfileService, PlayerProfileService>();
        services.AddScoped<IPlayerAnalysisService, PlayerAnalysisService>();
        services.AddScoped<IPlayerHistoryService, PlayerHistoryService>();
        services.AddScoped<IPlayerRosterService, PlayerRosterService>();
        services.AddScoped<IGalacticLegendProgressService, GalacticLegendProgressService>();
        services.AddScoped<ISquadService, SquadService>();
        services.AddScoped<IConquestService, ConquestService>();
        services.AddScoped<IConquestDailyPlanService, ConquestDailyPlanService>();
        services.AddSingleton<IGacRulesService, GacRulesService>();
        services.AddSingleton<IGacTelemetry, GacTelemetry>();
        services.AddScoped<IGacHistoryService, GacHistoryService>();
        services.AddScoped<IGacHistorySyncService, GacHistorySyncService>();
        services.AddScoped<IGacCounterStatisticsService, GacCounterStatisticsService>();
        services.AddScoped<IOpponentScoutingService, OpponentScoutingService>();
        services.AddSingleton<ICurrentGacScoutingCache, CurrentGacScoutingCache>();
        services.AddScoped<CurrentGacScoutingService>();
        services.AddScoped<ICurrentGacScoutingService, CachedCurrentGacScoutingService>();
        services.AddScoped<IGacPersonalLearningService, GacPersonalLearningService>();
        services.AddSingleton<GacPlannerWriteContext>();
        services.AddSingleton<GacOptimizationCoordinator>();
        services.AddScoped<GacPlannerService>();
        services.AddScoped<IGacPlannerService, LearningGacPlannerService>();
        services.AddScoped<IGacPlannerContextService, GacPlannerContextService>();
        services.AddScoped<IGacPlannerMutationService, GacPlannerMutationService>();
        services.AddScoped<GacDefenseStrategyService>();
        services.AddScoped<IGacDefenseStrategyService, GacDefenseStrategyService>();
        services.AddScoped<GacRosterDefenseCandidateService>();
        services.AddScoped<IGacSmartDefenseService, RosterAwareGacSmartDefenseService>();
        services.AddScoped<GacAttackPlanOptimizerService>();
        services.AddScoped<IGacAttackPlanOptimizerService, HardenedGacAttackPlanOptimizerService>();
        services.AddScoped<IGacJointRoundOptimizerService, GacJointRoundOptimizerService>();
        services.AddScoped<IGacAttackExecutionService, GacAttackExecutionService>();
        return services;
    }
}
