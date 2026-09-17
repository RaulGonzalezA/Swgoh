using Microsoft.Extensions.DependencyInjection;

using MongoDB.Driver;

using RepositoryMongoDb.DependencyInjection;

using Swgoh.Application.Investments;
using Swgoh.Infrastructure.Persistence;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure;

public static class InvestmentTargetDependencyInjection
{
    public static IServiceCollection AddInvestmentTargetInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        IndexKeysDefinition<InvestmentTargetDocument> indexKeys = Builders<InvestmentTargetDocument>.IndexKeys
            .Ascending(target => target.AllyCode)
            .Descending(target => target.UpdatedAtUtc);
        services.AddMongoRepository<InvestmentTargetDocument, string>(
            InvestmentTargetMongoRepository.CollectionName,
            target => target.Id,
            collection => collection
                .CreateIfMissing()
                .HasIndex(
                    indexKeys,
                    new CreateIndexOptions
                    {
                        Name = InvestmentTargetMongoRepository.AllyCodeUpdatedIndexName
                    }));
        services.AddSingleton<IInvestmentTargetRepository, InvestmentTargetMongoRepository>();
        return services;
    }
}
