using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public sealed record GacCounterStatisticsQuery(
    GacFormat Format,
    string? DefenderLeaderDefinitionId = null,
    bool? IsFleet = null,
    int MaxRounds = 1_000,
    int Limit = 100);

public sealed record GacCounterStatistics(
    bool IsFleet,
    string DefenderLeaderDefinitionId,
    IReadOnlyCollection<string> DefenderMemberDefinitionIds,
    string AttackerLeaderDefinitionId,
    IReadOnlyCollection<string> AttackerMemberDefinitionIds,
    int Uses,
    int Wins,
    decimal WinRate,
    int OneShots,
    decimal OneShotRate,
    decimal AverageBanners,
    decimal AverageAttempt,
    int PlayersObserved,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

public interface IGacCounterStatisticsService
{
    Task<IReadOnlyCollection<GacCounterStatistics>> GetAsync(
        GacCounterStatisticsQuery query,
        CancellationToken cancellationToken = default);
}

internal sealed class GacCounterStatisticsService(IGacHistoryRepository repository) : IGacCounterStatisticsService
{
    private const int MaxRounds = 5_000;
    private const int MaxResults = 500;

    public async Task<IReadOnlyCollection<GacCounterStatistics>> GetAsync(
        GacCounterStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Enum.IsDefined(query.Format))
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Format, "Unsupported GAC format.");
        }

        string? defenderLeader = string.IsNullOrWhiteSpace(query.DefenderLeaderDefinitionId)
            ? null
            : query.DefenderLeaderDefinitionId.Trim();
        int maxRounds = Math.Clamp(query.MaxRounds, 1, MaxRounds);
        int limit = Math.Clamp(query.Limit, 1, MaxResults);

        IReadOnlyCollection<GacHistoricalRound> rounds = await repository
            .GetRecentAsync(query.Format, maxRounds, cancellationToken)
            .ConfigureAwait(false);

        var observations = rounds
            .SelectMany(round => round.OffenseBattles.Select(battle => new CounterObservation(round, battle)))
            .Where(item => query.IsFleet is null || item.Battle.Defender.IsFleet == query.IsFleet.Value)
            .Where(item => defenderLeader is null || item.Battle.Defender.LeaderDefinitionId.Equals(
                defenderLeader,
                StringComparison.OrdinalIgnoreCase));

        return
        [
            .. observations
                .GroupBy(
                    item => $"{CompositionKey(item.Battle.Defender)}>{CompositionKey(item.Battle.Attacker)}",
                    StringComparer.Ordinal)
                .Select(BuildStatistics)
                .OrderByDescending(item => item.Uses)
                .ThenByDescending(item => item.WinRate)
                .ThenByDescending(item => item.OneShotRate)
                .ThenByDescending(item => item.AverageBanners)
                .Take(limit)
        ];
    }

    private static GacCounterStatistics BuildStatistics(IGrouping<string, CounterObservation> group)
    {
        CounterObservation first = group.First();
        GacOffenseBattle firstBattle = first.Battle;
        int uses = group.Count();
        int wins = group.Count(item => item.Battle.Won);
        int oneShots = group.Count(item => item.Battle.Won && item.Battle.Attempt == 1);

        return new GacCounterStatistics(
            firstBattle.Defender.IsFleet,
            firstBattle.Defender.LeaderDefinitionId,
            [.. firstBattle.Defender.MemberDefinitionIds],
            firstBattle.Attacker.LeaderDefinitionId,
            [.. firstBattle.Attacker.MemberDefinitionIds],
            uses,
            wins,
            Percent(wins, uses),
            oneShots,
            Percent(oneShots, uses),
            Round(group.Average(item => (decimal)item.Battle.Banners)),
            Round(group.Average(item => (decimal)item.Battle.Attempt)),
            group.Select(item => item.Round.AllyCode).Distinct().Count(),
            group.Min(item => item.Round.StartedAtUtc),
            group.Max(item => item.Round.StartedAtUtc));
    }

    private static string CompositionKey(GacHistoricalSquad squad)
    {
        string members = string.Join(
            ",",
            squad.MemberDefinitionIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        return $"{(squad.IsFleet ? "fleet" : "squad")}:{squad.LeaderDefinitionId}:{members}";
    }

    private static decimal Percent(int numerator, int denominator) => denominator == 0
        ? 0m
        : Round(numerator * 100m / denominator);

    private static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private sealed record CounterObservation(GacHistoricalRound Round, GacOffenseBattle Battle);
}
