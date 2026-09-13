using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Conquest;
using Swgoh.Domain.Conquest;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class ConquestPlanMongoRepository(
    IMongoDbRepository<ConquestPlanDocument, string> repository) : IConquestPlanRepository
{
    internal const string CollectionName = "conquestPlans";
    internal const string AllyCodeUpdatedIndexName = "ix_conquest_plans_ally_updated";

    public async Task<ConquestPlan?> FindByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ConquestPlanDocument? document = await repository
            .FindByIdAsync(id.Trim(), cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<ConquestPlan?> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<ConquestPlanDocument> filter = Builders<ConquestPlanDocument>.Filter
            .Eq(document => document.AllyCode, allyCode);
        SortDefinition<ConquestPlanDocument> sort = Builders<ConquestPlanDocument>.Sort
            .Descending(document => document.UpdatedAtUtc);
        IReadOnlyCollection<ConquestPlanDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit: 1, sort, cancellationToken)
            .ConfigureAwait(false);
        ConquestPlanDocument? document = documents.FirstOrDefault();
        return document is null ? null : ToDomain(document);
    }

    public Task UpsertAsync(ConquestPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return repository.UpsertAsync(ToDocument(plan), cancellationToken);
    }

    private static ConquestPlanDocument ToDocument(ConquestPlan plan) => new()
    {
        Id = plan.Id,
        AllyCode = plan.AllyCode,
        EventId = plan.EventId,
        Name = plan.Name,
        Difficulty = (int)plan.Difficulty,
        Feats =
        [
            .. plan.Feats.Select(feat => new ConquestFeatDocument
            {
                Id = feat.Id.ToString("D"),
                Name = feat.Name,
                Scope = (int)feat.Scope,
                Sector = feat.Sector,
                Points = feat.Points,
                Target = feat.Target,
                Progress = feat.Progress,
                ExpectedProgressPerBattle = feat.ExpectedProgressPerBattle,
                Rule = new ConquestFeatRuleDocument
                {
                    Type = (int)feat.Rule.Type,
                    Faction = feat.Rule.Faction,
                    UnitDefinitionIds = [.. feat.Rule.UnitDefinitionIds],
                    MinimumMatchingUnits = feat.Rule.MinimumMatchingUnits
                }
            })
        ],
        StaminaCostPerBattle = plan.StaminaCostPerBattle,
        ReserveFloorPercent = plan.ReserveFloorPercent,
        Stamina =
        [
            .. plan.Stamina.Select(value => new ConquestUnitStaminaDocument
            {
                DefinitionId = value.DefinitionId,
                CurrentPercent = value.CurrentPercent
            })
        ],
        CreatedAtUtc = plan.CreatedAtUtc,
        UpdatedAtUtc = plan.UpdatedAtUtc
    };

    private static ConquestPlan ToDomain(ConquestPlanDocument document) => ConquestPlan.Restore(
        document.AllyCode,
        document.EventId,
        document.Name,
        (ConquestDifficulty)document.Difficulty,
        document.Feats.Select(feat => ConquestFeat.Create(
            Guid.ParseExact(feat.Id, "D"),
            feat.Name,
            (ConquestFeatScope)feat.Scope,
            feat.Sector,
            feat.Points,
            feat.Target,
            feat.Progress,
            feat.ExpectedProgressPerBattle,
            ConquestFeatRule.Create(
                (ConquestFeatRuleType)feat.Rule.Type,
                feat.Rule.Faction,
                feat.Rule.UnitDefinitionIds,
                feat.Rule.MinimumMatchingUnits))),
        document.CreatedAtUtc,
        document.UpdatedAtUtc,
        document.StaminaCostPerBattle ?? ConquestPlan.DefaultStaminaCostPerBattle,
        document.ReserveFloorPercent ?? ConquestPlan.DefaultReserveFloorPercent,
        document.Stamina.Select(value => ConquestUnitStamina.Create(
            value.DefinitionId,
            value.CurrentPercent)));
}
