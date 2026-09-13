using System.Text.RegularExpressions;

using MongoDB.Bson;
using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Squads;
using Swgoh.Domain.Squads;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class SquadMongoRepository(IMongoDbRepository<SquadDefinitionDocument, Guid> repository) : ISquadRepository
{
    internal const string CollectionName = "squads";

    public async Task<SquadDefinition?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        SquadDefinitionDocument? document = await repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<IReadOnlyCollection<SquadDefinition>> SearchAsync(
        SquadSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filters = new List<FilterDefinition<SquadDefinitionDocument>>();
        FilterDefinitionBuilder<SquadDefinitionDocument> builder = Builders<SquadDefinitionDocument>.Filter;

        if (query.Format is SquadFormat format)
        {
            filters.Add(builder.Eq(document => document.Format, (int)format));
        }

        if (query.Use is SquadUse use)
        {
            filters.Add(builder.Eq(document => document.Use, (int)use));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            filters.Add(builder.AnyEq(document => document.Tags, query.Tag));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var expression = new BsonRegularExpression(Regex.Escape(query.Search), "i");
            filters.Add(builder.Or(
                builder.Regex(document => document.Name, expression),
                builder.Regex("Variants.Name", expression),
                builder.Regex("Variants.LeaderDefinitionId", expression),
                builder.Regex("Variants.MemberDefinitionIds", expression)));
        }

        FilterDefinition<SquadDefinitionDocument> filter = filters.Count == 0
            ? builder.Empty
            : builder.And(filters);
        SortDefinition<SquadDefinitionDocument> sort = Builders<SquadDefinitionDocument>.Sort
            .Ascending(document => document.Name)
            .Descending(document => document.UpdatedAtUtc);

        IReadOnlyCollection<SquadDefinitionDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, query.Limit, sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public Task UpsertAsync(SquadDefinition squad, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(squad);
        return repository.UpsertAsync(ToDocument(squad), cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        DeleteResult result = await repository.DeleteByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount > 0;
    }

    private static SquadDefinitionDocument ToDocument(SquadDefinition squad) => new()
    {
        Id = squad.Id,
        Name = squad.Name,
        Format = (int)squad.Format,
        Use = (int)squad.Use,
        Tags = [.. squad.Tags],
        Variants =
        [
            .. squad.Variants.Select(variant => new SquadVariantDocument
            {
                Key = variant.Key,
                Name = variant.Name,
                LeaderDefinitionId = variant.LeaderDefinitionId,
                MemberDefinitionIds = [.. variant.MemberDefinitionIds]
            })
        ],
        CreatedAtUtc = squad.CreatedAtUtc,
        UpdatedAtUtc = squad.UpdatedAtUtc
    };

    private static SquadDefinition ToDomain(SquadDefinitionDocument document)
    {
        var format = (SquadFormat)document.Format;
        SquadVariant[] variants =
        [
            .. document.Variants.Select(variant => SquadVariant.Create(
                format,
                variant.Key,
                variant.Name,
                variant.LeaderDefinitionId,
                variant.MemberDefinitionIds))
        ];

        return SquadDefinition.Restore(
            document.Id,
            document.Name,
            format,
            (SquadUse)document.Use,
            document.Tags,
            variants,
            document.CreatedAtUtc,
            document.UpdatedAtUtc);
    }
}
