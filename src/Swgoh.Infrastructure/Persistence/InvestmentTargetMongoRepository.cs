using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Investments;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class InvestmentTargetMongoRepository(
    IMongoDbRepository<InvestmentTargetDocument, string> repository) : IInvestmentTargetRepository
{
    internal const string CollectionName = "investmentTargets";
    internal const string AllyCodeUpdatedIndexName = "ix_investment_targets_ally_code_updated";

    public async Task<InvestmentTarget?> GetAsync(
        long allyCode,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        InvestmentTargetDocument? document = await repository
            .FindByIdAsync(ToId(allyCode, definitionId), cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<IReadOnlyCollection<InvestmentTarget>> GetAllAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<InvestmentTargetDocument> filter = Builders<InvestmentTargetDocument>.Filter
            .Eq(document => document.AllyCode, allyCode);
        SortDefinition<InvestmentTargetDocument> sort = Builders<InvestmentTargetDocument>.Sort
            .Descending(document => document.UpdatedAtUtc);
        IReadOnlyCollection<InvestmentTargetDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit: 250, sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public Task UpsertAsync(
        InvestmentTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return repository.UpsertAsync(ToDocument(target), cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        long allyCode,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        DeleteResult result = await repository
            .DeleteByIdAsync(ToId(allyCode, definitionId), cancellationToken)
            .ConfigureAwait(false);
        return result.DeletedCount > 0;
    }

    private static InvestmentTargetDocument ToDocument(InvestmentTarget target) => new()
    {
        Id = ToId(target.AllyCode, target.DefinitionId),
        AllyCode = target.AllyCode,
        DefinitionId = target.DefinitionId,
        TargetRelicTier = target.TargetRelicTier,
        TargetStars = target.TargetStars,
        CreatedAtUtc = target.CreatedAtUtc,
        UpdatedAtUtc = target.UpdatedAtUtc
    };

    private static InvestmentTarget ToDomain(InvestmentTargetDocument document) => new(
        document.AllyCode,
        document.DefinitionId,
        document.TargetRelicTier,
        document.TargetStars,
        document.CreatedAtUtc,
        document.UpdatedAtUtc);

    private static string ToId(long allyCode, string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        return $"{allyCode}:{definitionId.Trim().ToUpperInvariant()}";
    }
}
