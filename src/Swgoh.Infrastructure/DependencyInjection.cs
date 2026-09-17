using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MongoDB.Driver;

using RepositoryMongoDb.DependencyInjection;

using Swgoh.Application.Abstractions;
using Swgoh.Application.Conquest;
using Swgoh.Application.Gac;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Application.Squads;
using Swgoh.Application.TerritoryBattles;
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

        IndexKeysDefinition<GacBracketLocationDocument> gacBracketLocationIndexKeys =
            Builders<GacBracketLocationDocument>.IndexKeys
                .Ascending(location => location.AllyCode)
                .Descending(location => location.FoundAtUtc);
        services.AddMongoRepository<GacBracketLocationDocument, string>(
            GacBracketLocationMongoRepository.CollectionName,
            location => location.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    gacBracketLocationIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = GacBracketLocationMongoRepository.AllyCodeFoundAtIndexName
                    }));

        IndexKeysDefinition<GacTeamPresetDocument> gacTeamPresetIndexKeys = Builders<GacTeamPresetDocument>.IndexKeys
            .Ascending(preset => preset.AllyCode)
            .Ascending(preset => preset.Format)
            .Ascending(preset => preset.Use)
            .Ascending(preset => preset.Name);
        services.AddMongoRepository<GacTeamPresetDocument, string>(
            GacTeamPresetMongoRepository.CollectionName,
            preset => preset.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    gacTeamPresetIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = GacTeamPresetMongoRepository.AllyCodeFormatIndexName
                    }));

        IndexKeysDefinition<GacGeneratedTeamLifecycleDocument> generatedTeamLifecycleIndexKeys =
            Builders<GacGeneratedTeamLifecycleDocument>.IndexKeys
                .Ascending(entry => entry.AllyCode)
                .Ascending(entry => entry.Format)
                .Ascending(entry => entry.Origin)
                .Descending(entry => entry.CreatedAtUtc);
        services.AddMongoRepository<GacGeneratedTeamLifecycleDocument, string>(
            GacGeneratedTeamLifecycleMongoRepository.CollectionName,
            entry => entry.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    generatedTeamLifecycleIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = GacGeneratedTeamLifecycleMongoRepository.AllyCodeFormatIndexName
                    }));

        services.AddMongoRepository<GacDefenseStrategyDocument, string>(
            GacDefenseStrategyMongoRepository.CollectionName,
            strategy => strategy.Id,
            collection => collection.CreateIfMissing());

        IndexKeysDefinition<GacRoundPlanDocument> gacRoundPlanIndexKeys = Builders<GacRoundPlanDocument>.IndexKeys
            .Ascending(plan => plan.PlayerAllyCode)
            .Descending(plan => plan.UpdatedAtUtc);
        services.AddMongoRepository<GacRoundPlanDocument, string>(
            GacRoundPlanMongoRepository.CollectionName,
            plan => plan.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    gacRoundPlanIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = GacRoundPlanMongoRepository.PlayerUpdatedIndexName
                    }));

        IndexKeysDefinition<GacPersonalBattleDocument> personalBattleIndexKeys =
            Builders<GacPersonalBattleDocument>.IndexKeys
                .Ascending(battle => battle.PlayerAllyCode)
                .Ascending(battle => battle.Format)
                .Descending(battle => battle.RecordedAtUtc);
        IndexKeysDefinition<GacPersonalBattleDocument> personalBattleRoundIndexKeys =
            Builders<GacPersonalBattleDocument>.IndexKeys
                .Ascending(battle => battle.PlayerAllyCode)
                .Ascending(battle => battle.EventInstanceId)
                .Ascending(battle => battle.RoundNumber);
        services.AddMongoRepository<GacPersonalBattleDocument, string>(
            GacPersonalBattleMongoRepository.CollectionName,
            battle => battle.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    personalBattleIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = GacPersonalBattleMongoRepository.PlayerFormatRecordedIndexName
                    })
                .HasIndex(
                    personalBattleRoundIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = GacPersonalBattleMongoRepository.PlayerRoundIndexName
                    }));

        IndexKeysDefinition<GacLiveAttackStateDocument> liveAttackStateIndexKeys =
            Builders<GacLiveAttackStateDocument>.IndexKeys
                .Ascending(state => state.PlanId)
                .Ascending(state => state.DefenseId)
                .Ascending(state => state.Attempt)
                .Descending(state => state.RecordedAtUtc);
        services.AddMongoRepository<GacLiveAttackStateDocument, string>(
            GacLiveAttackStateMongoRepository.CollectionName,
            state => state.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    liveAttackStateIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = GacLiveAttackStateMongoRepository.PlanDefenseAttemptIndexName
                    }));

        IndexKeysDefinition<ConquestPlanDocument> conquestPlanIndexKeys = Builders<ConquestPlanDocument>.IndexKeys
            .Ascending(plan => plan.AllyCode)
            .Descending(plan => plan.UpdatedAtUtc);
        services.AddMongoRepository<ConquestPlanDocument, string>(
            ConquestPlanMongoRepository.CollectionName,
            plan => plan.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    conquestPlanIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = ConquestPlanMongoRepository.AllyCodeUpdatedIndexName
                    }));

        IndexKeysDefinition<RiseOfEmpireExecutionDocument> roteExecutionIndexKeys =
            Builders<RiseOfEmpireExecutionDocument>.IndexKeys
                .Ascending(execution => execution.GuildId)
                .Ascending(execution => execution.Status)
                .Descending(execution => execution.UpdatedAtUtc);
        services.AddMongoRepository<RiseOfEmpireExecutionDocument, string>(
            RiseOfEmpireExecutionMongoRepository.CollectionName,
            execution => execution.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    roteExecutionIndexKeys,
                    new CreateIndexOptions
                    {
                        Name = RiseOfEmpireExecutionMongoRepository.GuildStatusUpdatedIndexName
                    }));

        string gameDataBaseUrl = configuration["Swgoh:GameData:BaseUrl"]
            ?? "https://raw.githubusercontent.com/swgoh-utils/gamedata/main/";
        string gameDataLocale = configuration["Swgoh:GameData:Locale"] ?? "SPA_XM";
        services.AddHttpClient(SwgohGameDataCatalogClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(gameDataBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton(new SwgohGameDataOptions(gameDataLocale));
        services.AddSingleton<ISwgohGameDataCatalog, ConfiguredSwgohGameDataCatalog>();
        services.AddSingleton<IRosterGameDataCatalog, ConfiguredSwgohRosterGameDataCatalog>();
        services.AddSingleton<IDatacronSetCatalog, SwgohDatacronSetCatalogClient>();

        string statsBaseUrl = configuration["Swgoh:Stats:BaseUrl"] ?? "http://swgoh-stats:3223";
        services.AddHttpClient<ISwgohStatsClient, SwgohStatsClient>(client =>
        {
            client.BaseAddress = new Uri(statsBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        string comlinkBaseUrl = configuration["Swgoh:Comlink:BaseUrl"] ?? "http://comlink";
        services.AddSingleton<GacComlinkRequestLimiter>();
        services.AddTransient<GacComlinkRateLimitHandler>();
        services.AddHttpClient<SwgohComlinkClient>(client =>
        {
            client.BaseAddress = new Uri(comlinkBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(120);
        }).AddHttpMessageHandler<GacComlinkRateLimitHandler>();
        services.AddTransient<ISwgohPlayerClient, DatacronExpirationPlayerClient>();
        services.AddHttpClient(ComlinkGacClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(comlinkBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(120);
        }).AddHttpMessageHandler<GacComlinkRateLimitHandler>();

        string? gacHistoryProviderBaseUrl = configuration["Swgoh:GacHistory:ProviderBaseUrl"];
        if (!string.IsNullOrWhiteSpace(gacHistoryProviderBaseUrl))
        {
            string? gacHistoryProviderApiKey = configuration["Swgoh:GacHistory:ApiKey"];
            services.AddHttpClient(NormalizedHttpGacHistoryProvider.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(gacHistoryProviderBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddSingleton(new GacHistoryProviderOptions(gacHistoryProviderApiKey));
            services.AddSingleton<IGacHistoryProvider, NormalizedHttpGacHistoryProvider>();
        }

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPlayerRepository, PlayerMongoRepository>();
        services.AddSingleton<IPlayerSnapshotRepository, PlayerSnapshotMongoRepository>();
        services.AddSingleton<ISquadRepository, SquadMongoRepository>();
        services.AddSingleton<IConquestPlanRepository, ConquestPlanMongoRepository>();
        services.AddSingleton<IRiseOfEmpireExecutionRepository, RiseOfEmpireExecutionMongoRepository>();
        services.AddSingleton<IGacHistoryRepository, GacHistoryMongoRepository>();
        services.AddSingleton<IGacBracketLocationRepository, GacBracketLocationMongoRepository>();
        services.AddSingleton<IGacTeamPresetRepository, GacTeamPresetMongoRepository>();
        services.AddSingleton<IGacGeneratedTeamLifecycleRepository, GacGeneratedTeamLifecycleMongoRepository>();
        services.AddSingleton<IGacDefenseStrategyRepository, GacDefenseStrategyMongoRepository>();
        services.AddSingleton<IGacRoundPlanRepository, GacRoundPlanMongoRepository>();
        services.AddSingleton<IGacPersonalBattleRepository, GacPersonalBattleMongoRepository>();
        services.AddSingleton<IGacLiveAttackStateRepository, GacLiveAttackStateMongoRepository>();
        services.AddSingleton<IComlinkGacClient, ComlinkGacClient>();
        services.AddSingleton<GacExactBracketResolver>();
        services.AddSingleton<SwgohComlinkFastGacOpponentSource>();
        services.AddSingleton<ILiveGacOpponentSource, SwgohComlinkUnifiedGacOpponentSource>();
        services.AddSingleton<BackgroundGacOpponentSource>();
        services.AddSingleton<PersistedGacOpponentSource>();
        services.AddSingleton<ICurrentGacOpponentSource, PersistedGacOpponentSourceAdapter>();
        services.AddSingleton<ICurrentGacOpponentCache, PersistedGacOpponentCacheAdapter>();
        services.AddHostedService<BackgroundGacOpponentHostedService>();
        return services;
    }
}
