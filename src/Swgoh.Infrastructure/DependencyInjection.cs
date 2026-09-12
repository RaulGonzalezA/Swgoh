using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RepositoryMongoDb.DependencyInjection;

using Swgoh.Application.Abstractions;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;
using Swgoh.Infrastructure.Persistence;
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

        services.AddMongoRepository<PlayerProfile, long>(
            PlayerMongoRepository.CollectionName,
            player => player.AllyCode,
            collection => collection
                .CreateIfMissing()
                .HasIndex(player => player.AllyCode, indexName: "ux_players_ally_code", unique: true));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPlayerRepository, PlayerMongoRepository>();
        return services;
    }
}
