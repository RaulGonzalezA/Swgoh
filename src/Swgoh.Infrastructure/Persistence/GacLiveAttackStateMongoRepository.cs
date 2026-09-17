using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class GacLiveAttackStateMongoRepository(
    IMongoDbRepository<GacLiveAttackStateDocument, string> repository) : IGacLiveAttackStateRepository
{
    internal const string CollectionName = "gacLiveAttackStates";
    internal const string PlanDefenseAttemptIndexName = "ix_gac_live_attack_plan_defense_attempt";

    public async Task<IReadOnlyCollection<GacLiveAttackState>> GetByPlanAsync(
        string planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        FilterDefinition<GacLiveAttackStateDocument> filter = Builders<GacLiveAttackStateDocument>.Filter
            .Eq(document => document.PlanId, planId.Trim());
        SortDefinition<GacLiveAttackStateDocument> sort = Builders<GacLiveAttackStateDocument>.Sort
            .Ascending(document => document.DefenseId)
            .Ascending(document => document.Attempt)
            .Descending(document => document.RecordedAtUtc);
        IReadOnlyCollection<GacLiveAttackStateDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit: 500, sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public Task UpsertAsync(
        GacLiveAttackState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return repository.UpsertAsync(ToDocument(state), cancellationToken);
    }

    private static GacLiveAttackStateDocument ToDocument(GacLiveAttackState state) => new()
    {
        Id = state.Id,
        PlanId = state.PlanId,
        PlayerAllyCode = state.PlayerAllyCode,
        AttackId = state.AttackId.ToString("D"),
        DefenseId = state.DefenseId.ToString("D"),
        Attempt = state.Attempt,
        Status = (int)state.Status,
        Banners = state.Banners,
        Notes = state.Notes,
        RemainingEnemyUnitDefinitionIds = [.. state.RemainingEnemyUnitDefinitionIds],
        PreloadedTurnMeter = state.PreloadedTurnMeter,
        RecordedAtUtc = state.RecordedAtUtc
    };

    private static GacLiveAttackState ToDomain(GacLiveAttackStateDocument document) =>
        GacLiveAttackState.Create(
            document.PlanId,
            document.PlayerAllyCode,
            Guid.ParseExact(document.AttackId, "D"),
            Guid.ParseExact(document.DefenseId, "D"),
            document.Attempt,
            (GacAttackPlanStatus)document.Status,
            document.Banners,
            document.Notes,
            document.RemainingEnemyUnitDefinitionIds,
            document.PreloadedTurnMeter,
            document.RecordedAtUtc);
}
