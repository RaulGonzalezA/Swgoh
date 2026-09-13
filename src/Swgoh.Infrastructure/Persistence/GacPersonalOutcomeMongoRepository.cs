using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class GacPersonalOutcomeMongoRepository(
    IMongoDbRepository<GacPersonalRoundOutcomeDocument, string> repository) : IGacPersonalOutcomeRepository
{
    internal const string CollectionName = "gacPersonalOutcomes";
    internal const string AllyFormatUpdatedIndexName = "ix_gac_personal_outcomes_ally_format_updated";

    public Task UpsertAsync(
        GacPersonalRoundOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return repository.UpsertAsync(ToDocument(outcome), cancellationToken);
    }

    public async Task<IReadOnlyCollection<GacPersonalRoundOutcome>> GetRecentAsync(
        long allyCode,
        GacFormat format,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be between 1 and 500.");
        }

        FilterDefinitionBuilder<GacPersonalRoundOutcomeDocument> builder =
            Builders<GacPersonalRoundOutcomeDocument>.Filter;
        FilterDefinition<GacPersonalRoundOutcomeDocument> filter =
            builder.Eq(document => document.AllyCode, allyCode) &
            builder.Eq(document => document.Format, (int)format);
        SortDefinition<GacPersonalRoundOutcomeDocument> sort =
            Builders<GacPersonalRoundOutcomeDocument>.Sort.Descending(document => document.UpdatedAtUtc);
        IReadOnlyCollection<GacPersonalRoundOutcomeDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit, sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    private static GacPersonalRoundOutcomeDocument ToDocument(GacPersonalRoundOutcome outcome) => new()
    {
        Id = outcome.Id,
        AllyCode = outcome.AllyCode,
        OpponentAllyCode = outcome.OpponentAllyCode,
        EventInstanceId = outcome.EventInstanceId,
        RoundNumber = outcome.RoundNumber,
        Format = (int)outcome.Format,
        UpdatedAtUtc = outcome.UpdatedAtUtc,
        Attacks =
        [
            .. outcome.Attacks.Select(attack => new GacPersonalAttackOutcomeDocument
            {
                AttackId = attack.AttackId.ToString("D"),
                Attempt = attack.Attempt,
                Status = (int)attack.Status,
                DefenseSquad = ToDocument(attack.DefenseSquad),
                AttackSquad = ToDocument(attack.AttackSquad)
            })
        ]
    };

    private static GacPersonalRoundOutcome ToDomain(GacPersonalRoundOutcomeDocument document)
    {
        var format = (GacFormat)document.Format;
        return new GacPersonalRoundOutcome(
            document.Id,
            document.AllyCode,
            document.OpponentAllyCode,
            document.EventInstanceId,
            document.RoundNumber,
            format,
            [
                .. document.Attacks.Select(attack => new GacPersonalAttackOutcome(
                    Guid.ParseExact(attack.AttackId, "D"),
                    attack.Attempt,
                    (GacAttackPlanStatus)attack.Status,
                    ToDomain(attack.DefenseSquad, format),
                    ToDomain(attack.AttackSquad, format)))
            ],
            document.UpdatedAtUtc);
    }

    private static GacPlannerSquadDocument ToDocument(GacPlannerSquad squad) => new()
    {
        LeaderDefinitionId = squad.LeaderDefinitionId,
        MemberDefinitionIds = [.. squad.MemberDefinitionIds],
        IsFleet = squad.IsFleet
    };

    private static GacPlannerSquad ToDomain(GacPlannerSquadDocument document, GacFormat format) =>
        GacPlannerSquad.Create(
            format,
            document.LeaderDefinitionId,
            document.MemberDefinitionIds,
            document.IsFleet);
}
