using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.TerritoryBattles;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class RiseOfEmpireExecutionMongoRepository(
    IMongoDbRepository<RiseOfEmpireExecutionDocument, string> repository) : IRiseOfEmpireExecutionRepository
{
    internal const string CollectionName = "riseOfEmpireExecutions";
    internal const string GuildStatusUpdatedIndexName = "ix_rote_execution_guild_status_updated";

    public async Task<RiseOfEmpireExecutionSession?> GetActiveAsync(
        string guildId,
        CancellationToken cancellationToken = default)
    {
        FilterDefinitionBuilder<RiseOfEmpireExecutionDocument> builder = Builders<RiseOfEmpireExecutionDocument>.Filter;
        FilterDefinition<RiseOfEmpireExecutionDocument> filter =
            builder.Eq(document => document.GuildId, guildId)
            & builder.Eq(document => document.Status, (int)RiseOfEmpireExecutionStatus.Active);
        SortDefinition<RiseOfEmpireExecutionDocument> sort = Builders<RiseOfEmpireExecutionDocument>.Sort
            .Descending(document => document.UpdatedAtUtc);
        IReadOnlyCollection<RiseOfEmpireExecutionDocument> documents = await repository
            .FindPageAsync(filter, 0, 1, sort, cancellationToken)
            .ConfigureAwait(false);
        RiseOfEmpireExecutionDocument? document = documents.FirstOrDefault();
        return document is null ? null : ToDomain(document);
    }

    public async Task<RiseOfEmpireExecutionSession?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        RiseOfEmpireExecutionDocument? document = await repository
            .FindByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<IReadOnlyCollection<RiseOfEmpireExecutionSession>> GetHistoryAsync(
        string guildId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<RiseOfEmpireExecutionDocument> filter = Builders<RiseOfEmpireExecutionDocument>.Filter
            .Eq(document => document.GuildId, guildId);
        SortDefinition<RiseOfEmpireExecutionDocument> sort = Builders<RiseOfEmpireExecutionDocument>.Sort
            .Descending(document => document.CreatedAtUtc);
        IReadOnlyCollection<RiseOfEmpireExecutionDocument> documents = await repository
            .FindPageAsync(filter, 0, Math.Clamp(limit, 1, 100), sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public Task UpsertAsync(RiseOfEmpireExecutionSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return repository.UpsertAsync(ToDocument(session), cancellationToken);
    }

    private static RiseOfEmpireExecutionDocument ToDocument(RiseOfEmpireExecutionSession session) => new()
    {
        Id = session.Id,
        GuildId = session.GuildId,
        GuildName = session.GuildName,
        Label = session.Label,
        Status = (int)session.Status,
        CreatedAtUtc = session.CreatedAtUtc,
        UpdatedAtUtc = session.UpdatedAtUtc,
        ClosedAtUtc = session.ClosedAtUtc,
        Results =
        [
            .. session.Results.Select(result => new RiseOfEmpireMissionExecutionResultDocument
            {
                PlayerAllyCode = result.PlayerAllyCode,
                PlayerName = result.PlayerName,
                Phase = result.Phase,
                PlanetId = result.PlanetId,
                PlanetName = result.PlanetName,
                MissionId = result.MissionId,
                MissionName = result.MissionName,
                TeamName = result.TeamName,
                State = (int)result.State,
                CompletedWaves = result.CompletedWaves,
                TotalWaves = result.TotalWaves,
                TerritoryPoints = result.TerritoryPoints,
                Notes = result.Notes,
                UpdatedAtUtc = result.UpdatedAtUtc
            })
        ]
    };

    private static RiseOfEmpireExecutionSession ToDomain(RiseOfEmpireExecutionDocument document) => new(
        document.Id,
        document.GuildId,
        document.GuildName,
        document.Label,
        (RiseOfEmpireExecutionStatus)document.Status,
        document.CreatedAtUtc,
        document.UpdatedAtUtc,
        document.ClosedAtUtc,
        [
            .. document.Results.Select(result => new RiseOfEmpireMissionExecutionResult(
                result.PlayerAllyCode,
                result.PlayerName,
                result.Phase,
                result.PlanetId,
                result.PlanetName,
                result.MissionId,
                result.MissionName,
                result.TeamName,
                (RiseOfEmpireMissionExecutionState)result.State,
                result.CompletedWaves,
                result.TotalWaves,
                result.TerritoryPoints,
                result.Notes,
                result.UpdatedAtUtc))
        ]);
}
