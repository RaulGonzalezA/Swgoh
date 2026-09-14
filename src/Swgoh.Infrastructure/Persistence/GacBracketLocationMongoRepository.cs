using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class GacBracketLocationMongoRepository(
    IMongoDbRepository<GacBracketLocationDocument, string> repository) : IGacBracketLocationRepository
{
    internal const string CollectionName = "gacBracketLocations";
    internal const string AllyCodeFoundAtIndexName = "ix_gac_bracket_locations_ally_found_at";

    public async Task<GacBracketLocation?> FindAsync(
        long allyCode,
        string eventInstanceId,
        GacLeague league,
        CancellationToken cancellationToken = default)
    {
        string id = GacBracketLocation.CreateId(allyCode, eventInstanceId, league);
        GacBracketLocationDocument? document = await repository
            .FindByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<GacBracketLocation?> FindLatestAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<GacBracketLocationDocument> filter = Builders<GacBracketLocationDocument>.Filter
            .Eq(document => document.AllyCode, allyCode);
        SortDefinition<GacBracketLocationDocument> sort = Builders<GacBracketLocationDocument>.Sort
            .Descending(document => document.FoundAtUtc);
        List<GacBracketLocationDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit: 1, sort, cancellationToken)
            .ConfigureAwait(false);
        return documents.Count == 0 ? null : ToDomain(documents[0]);
    }

    public Task UpsertAsync(
        GacBracketLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        return repository.UpsertAsync(ToDocument(location), cancellationToken);
    }

    private static GacBracketLocationDocument ToDocument(GacBracketLocation location) => new()
    {
        Id = location.Id,
        AllyCode = location.AllyCode,
        EventId = location.EventId,
        EventInstanceId = location.EventInstanceId,
        League = (int)location.League,
        Format = (int)location.Format,
        BracketIndex = location.BracketIndex,
        SkillRating = location.SkillRating,
        FoundAtUtc = location.FoundAtUtc
    };

    private static GacBracketLocation ToDomain(GacBracketLocationDocument document) => new(
        document.AllyCode,
        document.EventId,
        document.EventInstanceId,
        (GacLeague)document.League,
        (GacFormat)document.Format,
        document.BracketIndex,
        document.SkillRating,
        document.FoundAtUtc);
}
