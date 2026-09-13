using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class GacPersonalBattleMongoRepository(
    IMongoDbRepository<GacPersonalBattleDocument, string> repository) : IGacPersonalBattleRepository
{
    internal const string CollectionName = "gacPersonalBattles";
    internal const string PlayerFormatRecordedIndexName = "ix_gac_personal_battles_player_format_recorded";
    internal const string PlayerRoundIndexName = "ix_gac_personal_battles_player_round";

    public async Task<GacPersonalBattleObservation?> FindByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        GacPersonalBattleDocument? document = await repository
            .FindByIdAsync(id.Trim(), cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetAsync(
        long playerAllyCode,
        GacFormat format,
        int limit = 1_000,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 5_000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        FilterDefinitionBuilder<GacPersonalBattleDocument> builder = Builders<GacPersonalBattleDocument>.Filter;
        FilterDefinition<GacPersonalBattleDocument> filter =
            builder.Eq(document => document.PlayerAllyCode, playerAllyCode) &
            builder.Eq(document => document.Format, (int)format);
        SortDefinition<GacPersonalBattleDocument> sort = Builders<GacPersonalBattleDocument>.Sort
            .Descending(document => document.RecordedAtUtc);
        IReadOnlyCollection<GacPersonalBattleDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit, sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public async Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetRoundAsync(
        long playerAllyCode,
        string eventInstanceId,
        int roundNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventInstanceId);
        FilterDefinitionBuilder<GacPersonalBattleDocument> builder = Builders<GacPersonalBattleDocument>.Filter;
        FilterDefinition<GacPersonalBattleDocument> filter =
            builder.Eq(document => document.PlayerAllyCode, playerAllyCode) &
            builder.Eq(document => document.EventInstanceId, eventInstanceId.Trim()) &
            builder.Eq(document => document.RoundNumber, roundNumber);
        SortDefinition<GacPersonalBattleDocument> sort = Builders<GacPersonalBattleDocument>.Sort
            .Ascending(document => document.Attempt);
        IReadOnlyCollection<GacPersonalBattleDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit: 100, sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public Task UpsertAsync(
        GacPersonalBattleObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return repository.UpsertAsync(ToDocument(observation), cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await repository.DeleteByIdAsync(id.Trim(), cancellationToken).ConfigureAwait(false);
    }

    private static GacPersonalBattleDocument ToDocument(GacPersonalBattleObservation observation) => new()
    {
        Id = observation.Id,
        PlayerAllyCode = observation.PlayerAllyCode,
        OpponentAllyCode = observation.OpponentAllyCode,
        EventInstanceId = observation.EventInstanceId,
        RoundNumber = observation.RoundNumber,
        Format = (int)observation.Format,
        AttackId = observation.AttackId.ToString("D"),
        DefenseId = observation.DefenseId.ToString("D"),
        Attempt = observation.Attempt,
        IsFleet = observation.IsFleet,
        AttackerDefinitionIds = [.. observation.AttackerDefinitionIds],
        DefenderDefinitionIds = [.. observation.DefenderDefinitionIds],
        Won = observation.Won,
        Banners = observation.Banners,
        RecordedAtUtc = observation.RecordedAtUtc
    };

    private static GacPersonalBattleObservation ToDomain(GacPersonalBattleDocument document) =>
        GacPersonalBattleObservation.Create(
            document.PlayerAllyCode,
            document.OpponentAllyCode,
            document.EventInstanceId,
            document.RoundNumber,
            (GacFormat)document.Format,
            Guid.ParseExact(document.AttackId, "D"),
            Guid.ParseExact(document.DefenseId, "D"),
            document.Attempt,
            document.IsFleet,
            document.AttackerDefinitionIds,
            document.DefenderDefinitionIds,
            document.Won,
            document.Banners,
            document.RecordedAtUtc);
}
