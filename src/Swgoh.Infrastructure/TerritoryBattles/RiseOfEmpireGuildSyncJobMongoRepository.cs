using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.TerritoryBattles;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.TerritoryBattles;

internal sealed class RiseOfEmpireGuildSyncJobMongoRepository(
    IMongoDbRepository<RiseOfEmpireGuildSyncJobDocument, string> repository) : IRiseOfEmpireGuildSyncJobRepository
{
    internal const string CollectionName = "riseOfEmpireGuildSyncJobs";

    public async Task<RiseOfEmpireGuildSyncJob?> FindByIdAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        RiseOfEmpireGuildSyncJobDocument? document = await repository
            .FindByIdAsync(jobId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<RiseOfEmpireGuildSyncJob?> FindLatestByAllyCodeAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<RiseOfEmpireGuildSyncJobDocument> filter = Builders<RiseOfEmpireGuildSyncJobDocument>.Filter
            .Eq(document => document.AllyCode, allyCode);
        SortDefinition<RiseOfEmpireGuildSyncJobDocument> sort = Builders<RiseOfEmpireGuildSyncJobDocument>.Sort
            .Descending(document => document.CreatedAtUtc);
        List<RiseOfEmpireGuildSyncJobDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, 1, sort, cancellationToken)
            .ConfigureAwait(false);
        return documents.Count == 0 ? null : ToDomain(documents[0]);
    }

    public async Task<IReadOnlyCollection<RiseOfEmpireGuildSyncJob>> FindRecoverableAsync(
        int maxJobs = 16,
        CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(maxJobs, 1, 64);
        int[] recoverableStatuses =
        [
            (int)RiseOfEmpireGuildSyncStatus.Queued,
            (int)RiseOfEmpireGuildSyncStatus.DiscoveringMembers,
            (int)RiseOfEmpireGuildSyncStatus.RefreshingMembers,
            (int)RiseOfEmpireGuildSyncStatus.BuildingPlan
        ];
        FilterDefinition<RiseOfEmpireGuildSyncJobDocument> filter = Builders<RiseOfEmpireGuildSyncJobDocument>.Filter
            .In(document => document.Status, recoverableStatuses);
        SortDefinition<RiseOfEmpireGuildSyncJobDocument> sort = Builders<RiseOfEmpireGuildSyncJobDocument>.Sort
            .Ascending(document => document.CreatedAtUtc);
        List<RiseOfEmpireGuildSyncJobDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit, sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public Task UpsertAsync(
        RiseOfEmpireGuildSyncJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return repository.UpsertAsync(ToDocument(job), cancellationToken);
    }

    private static RiseOfEmpireGuildSyncJobDocument ToDocument(RiseOfEmpireGuildSyncJob job) => new()
    {
        Id = job.Id,
        AllyCode = job.AllyCode,
        Status = (int)job.Status,
        TotalMembers = job.TotalMembers,
        CompletedMembers = job.CompletedMembers,
        FailedMembers = job.FailedMembers,
        CurrentMember = job.CurrentMember,
        GuildId = job.GuildId,
        GuildName = job.GuildName,
        CreatedAtUtc = job.CreatedAtUtc,
        UpdatedAtUtc = job.UpdatedAtUtc,
        StartedAtUtc = job.StartedAtUtc,
        CompletedAtUtc = job.CompletedAtUtc,
        Error = job.Error
    };

    private static RiseOfEmpireGuildSyncJob ToDomain(RiseOfEmpireGuildSyncJobDocument document) => new(
        document.Id,
        document.AllyCode,
        (RiseOfEmpireGuildSyncStatus)document.Status,
        document.TotalMembers,
        document.CompletedMembers,
        document.FailedMembers,
        document.CurrentMember,
        document.GuildId,
        document.GuildName,
        document.CreatedAtUtc,
        document.UpdatedAtUtc,
        document.StartedAtUtc,
        document.CompletedAtUtc,
        document.Error);
}
