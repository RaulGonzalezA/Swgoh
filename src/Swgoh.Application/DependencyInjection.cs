using Microsoft.Extensions.DependencyInjection;

using Swgoh.Application.Conquest;
using Swgoh.Application.Eras;
using Swgoh.Application.Gac;
using Swgoh.Application.Investments;
using Swgoh.Application.Players;
using Swgoh.Application.Squads;
using Swgoh.Application.TerritoryBattles;

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
        services.AddScoped<IEraService, EraService>();
        services.AddScoped<IRiseOfEmpireService, RiseOfEmpireService>();
        services.AddScoped<IRiseOfEmpireMissionGuideService, RiseOfEmpireMissionGuideService>();
        services.AddScoped<IRiseOfEmpireGuildService, RiseOfEmpireGuildService>();
        services.AddScoped<IRiseOfEmpireExecutionService, RiseOfEmpireExecutionService>();
        services.AddScoped<IRiseOfEmpireExecutionProgressService, RiseOfEmpireExecutionProgressService>();
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
        services.AddScoped<IGacGeneratedTeamLifecycleService, GacGeneratedTeamLifecycleService>();
        services.AddScoped<GacPlannerService>();
        services.AddScoped<LearningGacPlannerService>();
        services.AddScoped<IGacPlannerService, ExecutionAwareGacPlannerService>();
        services.AddScoped<IGacPlannerContextService, GacPlannerContextService>();
        services.AddScoped<IGacPlannerMutationService, GacPlannerMutationService>();
        services.AddScoped<GacDefenseStrategyService>();
        services.AddScoped<IGacDefenseStrategyService, GacDefenseStrategyService>();
        services.AddScoped<IGacGeneratedDefenseCleanupService, GacGeneratedDefenseCleanupService>();
        services.AddScoped<IGacRosterDefenseCandidateProvider, OptimizedGacRosterDefenseCandidateProvider>();
        services.AddScoped<IGacRosterAttackCandidateProvider, GacRosterAttackCandidateProvider>();
        services.AddScoped<IGacSmartDefenseService, RosterAwareGacSmartDefenseService>();
        services.AddScoped<GacAttackPlanOptimizerService>();
        services.AddScoped<IGacAttackPlanOptimizerService, HardenedGacAttackPlanOptimizerService>();
        services.AddScoped<IGacJointRoundOptimizerService, GacJointRoundOptimizerService>();
        services.AddScoped<IGacAttackExecutionService, GacAttackExecutionService>();
        services.AddScoped<IPlayerInventoryService, PlayerInventoryService>();
        services.AddScoped<IInvestmentOptimizerService, InvestmentOptimizerService>();
        return services;
    }
}
