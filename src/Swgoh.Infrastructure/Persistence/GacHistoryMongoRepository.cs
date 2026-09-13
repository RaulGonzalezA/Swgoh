using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class GacHistoryMongoRepository(IMongoDbRepository<GacHistoryRoundDocument, string> repository)
    : IGacHistoryRepository
{
    internal const string CollectionName = "gacHistory";
    internal const string AllyCodeFormatStartedIndexName = "ix_gac_history_ally_format_started";
    internal const string FormatStartedIndexName = "ix_gac_history_format_started";

    public async Task UpsertManyAsync(
        IReadOnlyCollection<GacHistoricalRound> rounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        foreach (GacHistoricalRound round in rounds)
        {
            await repository.UpsertAsync(ToDocument(round), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyCollection<GacHistoricalRound>> GetAsync(
        long allyCode,
        GacFormat? format,
        int maxRounds,
        CancellationToken cancellationToken = default)
    {
        FilterDefinitionBuilder<GacHistoryRoundDocument> builder = Builders<GacHistoryRoundDocument>.Filter;
        FilterDefinition<GacHistoryRoundDocument> filter = builder.Eq(document => document.AllyCode, allyCode);
        if (format is GacFormat requestedFormat)
        {
            filter &= builder.Eq(document => document.Format, (int)requestedFormat);
        }

        IReadOnlyCollection<GacHistoryRoundDocument> documents = await FindRecentAsync(
            filter,
            maxRounds,
            cancellationToken).ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public async Task<IReadOnlyCollection<GacHistoricalRound>> GetRecentAsync(
        GacFormat format,
        int maxRounds,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<GacHistoryRoundDocument> filter = Builders<GacHistoryRoundDocument>.Filter
            .Eq(document => document.Format, (int)format);
        IReadOnlyCollection<GacHistoryRoundDocument> documents = await FindRecentAsync(
            filter,
            maxRounds,
            cancellationToken).ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    private Task<IReadOnlyCollection<GacHistoryRoundDocument>> FindRecentAsync(
        FilterDefinition<GacHistoryRoundDocument> filter,
        int maxRounds,
        CancellationToken cancellationToken)
    {
        SortDefinition<GacHistoryRoundDocument> sort = Builders<GacHistoryRoundDocument>.Sort
            .Descending(document => document.StartedAtUtc)
            .Descending(document => document.Season)
            .Descending(document => document.EventNumber)
            .Descending(document => document.RoundNumber);
        return repository.FindPageAsync(filter, skip: 0, maxRounds, sort, cancellationToken);
    }

    private static GacHistoryRoundDocument ToDocument(GacHistoricalRound round) => new()
    {
        Id = round.Id,
        AllyCode = round.AllyCode,
        Season = round.Season,
        EventNumber = round.EventNumber,
        RoundNumber = round.RoundNumber,
        Format = (int)round.Format,
        League = (int)round.League,
        StartedAtUtc = round.StartedAtUtc,
        FullClear = round.FullClear,
        Source = round.Source,
        Defenses = [.. round.Defenses.Select(ToDocument)],
        OffenseBattles = [.. round.OffenseBattles.Select(ToDocument)]
    };

    private static GacDefensePlacementDocument ToDocument(GacDefensePlacement placement) => new()
    {
        Zone = placement.Zone,
        Squad = ToDocument(placement.Squad),
        Holds = placement.Holds,
        Defeated = placement.Defeated
    };

    private static GacOffenseBattleDocument ToDocument(GacOffenseBattle battle) => new()
    {
        Zone = battle.Zone,
        Defender = ToDocument(battle.Defender),
        Attacker = ToDocument(battle.Attacker),
        Won = battle.Won,
        Banners = battle.Banners,
        Attempt = battle.Attempt,
        AttackedAtUtc = battle.AttackedAtUtc
    };

    private static GacHistoricalSquadDocument ToDocument(GacHistoricalSquad squad) => new()
    {
        LeaderDefinitionId = squad.LeaderDefinitionId,
        MemberDefinitionIds = [.. squad.MemberDefinitionIds],
        IsFleet = squad.IsFleet
    };

    private static GacHistoricalRound ToDomain(GacHistoryRoundDocument document) => GacHistoricalRound.Create(
        document.AllyCode,
        document.Season,
        document.EventNumber,
        document.RoundNumber,
        (GacFormat)document.Format,
        (GacLeague)document.League,
        document.StartedAtUtc,
        document.FullClear,
        document.Source,
        document.Defenses.Select(ToDomain),
        document.OffenseBattles.Select(ToDomain));

    private static GacDefensePlacement ToDomain(GacDefensePlacementDocument document) => GacDefensePlacement.Create(
        document.Zone,
        ToDomain(document.Squad),
        document.Holds,
        document.Defeated);

    private static GacOffenseBattle ToDomain(GacOffenseBattleDocument document) => GacOffenseBattle.Create(
        document.Zone,
        ToDomain(document.Defender),
        ToDomain(document.Attacker),
        document.Won,
        document.Banners,
        document.Attempt,
        document.AttackedAtUtc);

    private static GacHistoricalSquad ToDomain(GacHistoricalSquadDocument document) => GacHistoricalSquad.Create(
        document.LeaderDefinitionId,
        document.MemberDefinitionIds,
        document.IsFleet);
}
