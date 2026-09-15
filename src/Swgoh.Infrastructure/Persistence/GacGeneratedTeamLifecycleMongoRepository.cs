using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class GacGeneratedTeamLifecycleMongoRepository(
    IMongoDbRepository<GacGeneratedTeamLifecycleDocument, string> repository) : IGacGeneratedTeamLifecycleRepository
{
    internal const string CollectionName = "gacGeneratedTeamLifecycle";
    internal const string AllyCodeFormatIndexName = "ix_gac_generated_team_lifecycle_ally_format";

    public async Task<IReadOnlyCollection<GacGeneratedTeamLifecycleEntry>> GetAsync(
        long allyCode,
        GacFormat? format,
        CancellationToken cancellationToken = default)
    {
        FilterDefinitionBuilder<GacGeneratedTeamLifecycleDocument> builder = Builders<GacGeneratedTeamLifecycleDocument>.Filter;
        FilterDefinition<GacGeneratedTeamLifecycleDocument> filter = builder.Eq(document => document.AllyCode, allyCode);
        if (format is GacFormat requestedFormat)
        {
            filter &= builder.Eq(document => document.Format, (int)requestedFormat);
        }

        SortDefinition<GacGeneratedTeamLifecycleDocument> sort = Builders<GacGeneratedTeamLifecycleDocument>.Sort
            .Descending(document => document.CreatedAtUtc);
        IReadOnlyCollection<GacGeneratedTeamLifecycleDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit: 1000, sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public Task UpsertAsync(
        GacGeneratedTeamLifecycleEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return repository.UpsertAsync(ToDocument(entry), cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid presetId,
        CancellationToken cancellationToken = default)
    {
        DeleteResult result = await repository
            .DeleteByIdAsync(ToDocumentId(presetId), cancellationToken)
            .ConfigureAwait(false);
        return result.DeletedCount > 0;
    }

    private static GacGeneratedTeamLifecycleDocument ToDocument(GacGeneratedTeamLifecycleEntry entry) => new()
    {
        Id = ToDocumentId(entry.PresetId),
        AllyCode = entry.AllyCode,
        Format = (int)entry.Format,
        Origin = (int)entry.Origin,
        GenerationId = entry.GenerationId,
        RoundPlanId = entry.RoundPlanId,
        CreatedAtUtc = entry.CreatedAtUtc
    };

    private static GacGeneratedTeamLifecycleEntry ToDomain(GacGeneratedTeamLifecycleDocument document) => new(
        Guid.ParseExact(document.Id, "D"),
        document.AllyCode,
        (GacFormat)document.Format,
        (GacGeneratedTeamOrigin)document.Origin,
        document.GenerationId,
        document.RoundPlanId,
        document.CreatedAtUtc);

    private static string ToDocumentId(Guid id) => id.ToString("D");
}
