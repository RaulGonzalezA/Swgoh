using Swgoh.Application.GameData;
using Swgoh.Application.Squads;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Squads;

namespace Swgoh.Application.Gac;

internal sealed class OpponentScoutingService(
    IGacHistoryRepository historyRepository,
    ISwgohGameDataCatalog gameDataCatalog,
    ISquadRepository squadRepository,
    IGacRulesService rulesService) : IOpponentScoutingService
{
    private const int MaxRounds = 200;

    public async Task<OpponentScoutingReport?> GetAsync(
        long allyCode,
        GacFormat format,
        GacLeague? targetLeague,
        int maxRounds,
        CancellationToken cancellationToken = default)
    {
        ValidateAllyCode(allyCode);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.");
        }

        if (targetLeague is GacLeague requestedLeague && !Enum.IsDefined(requestedLeague))
        {
            throw new ArgumentOutOfRangeException(nameof(targetLeague), targetLeague, "Unsupported GAC league.");
        }

        int queryLimit = Math.Clamp(maxRounds, 1, MaxRounds);
        IReadOnlyCollection<GacHistoricalRound> history = await historyRepository
            .GetAsync(allyCode, format, queryLimit, cancellationToken)
            .ConfigureAwait(false);
        if (history.Count == 0)
        {
            return null;
        }

        GacHistoricalRound[] rounds = [.. history.OrderByDescending(round => round.StartedAtUtc)];
        GacHistoricalRound latestRound = rounds[0];
        GacLeague effectiveTargetLeague = targetLeague ?? latestRound.League;
        GacDefenseRequirements requirements = rulesService.GetDefenseRequirements(effectiveTargetLeague, format);
        GacLeagueTransition transition = rulesService.CompareLeagues(latestRound.League, effectiveTargetLeague, format);

        Task<GameDataCatalog> catalogTask = gameDataCatalog.GetAsync(cancellationToken);
        Task<IReadOnlyCollection<SquadDefinition>> squadsTask = squadRepository.SearchAsync(
            new SquadSearchQuery(Format: ToSquadFormat(format), Limit: 200),
            cancellationToken);
        await Task.WhenAll(catalogTask, squadsTask).ConfigureAwait(false);

        GameDataCatalog catalog = await catalogTask.ConfigureAwait(false);
        IReadOnlyCollection<SquadDefinition> squadDefinitions = await squadsTask.ConfigureAwait(false);
        Dictionary<string, GameUnitDefinition> units = catalog.Units.Values
            .GroupBy(unit => unit.BaseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ArchetypeMatch> archetypes = BuildArchetypeMap(squadDefinitions);

        GacDefensePatternDetails[] defensePatterns = BuildDefensePatterns(rounds, units, archetypes);
        GacCounterPatternDetails[] counterPatterns = BuildCounterPatterns(rounds, units);
        GacPredictedDefenseDetails[] predictedSquads = BuildPredictions(
            defensePatterns.Where(pattern => !pattern.IsFleet),
            requirements.SquadDefenseCount);
        GacPredictedDefenseDetails[] predictedFleets = BuildPredictions(
            defensePatterns.Where(pattern => pattern.IsFleet),
            requirements.FleetDefenseCount);

        bool?[] fullClears = [.. rounds.Select(round => round.FullClear).Where(value => value.HasValue)];
        decimal? fullClearRate = fullClears.Length == 0
            ? null
            : Percent(fullClears.Count(value => value is true), fullClears.Length);

        double[] firstAttackDelays =
        [
            .. rounds.Select(round => GetFirstAttackDelayMinutes(round))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
        ];
        decimal? averageFirstAttackDelay = firstAttackDelays.Length == 0
            ? null
            : Round((decimal)firstAttackDelays.Average());

        return new OpponentScoutingReport(
            allyCode,
            format,
            rounds.Length,
            rounds.Select(round => round.Season).Distinct().Count(),
            rounds.Min(round => round.StartedAtUtc),
            rounds.Max(round => round.StartedAtUtc),
            latestRound.League,
            effectiveTargetLeague,
            requirements.SquadDefenseCount,
            requirements.FleetDefenseCount,
            transition.AdditionalSquadDefenses,
            transition.AdditionalFleetDefenses,
            fullClearRate,
            averageFirstAttackDelay,
            defensePatterns,
            counterPatterns,
            predictedSquads,
            predictedFleets);
    }

    private static GacDefensePatternDetails[] BuildDefensePatterns(
        IReadOnlyCollection<GacHistoricalRound> rounds,
        IReadOnlyDictionary<string, GameUnitDefinition> units,
        IReadOnlyDictionary<string, ArchetypeMatch> archetypes)
    {
        var observations = rounds
            .SelectMany(round => round.Defenses.Select(placement => new DefenseObservation(round.Id, placement)))
            .GroupBy(observation => CompositionKey(observation.Placement.Squad), StringComparer.Ordinal)
            .Select(group =>
            {
                DefenseObservation first = group.First();
                GacHistoricalSquad squad = first.Placement.Squad;
                int appearances = group.Count();
                int roundsPlaced = group.Select(item => item.RoundId).Distinct(StringComparer.Ordinal).Count();
                archetypes.TryGetValue(group.Key, out ArchetypeMatch? archetype);
                ZoneFrequency[] zones =
                [
                    .. group.GroupBy(item => item.Placement.Zone, StringComparer.OrdinalIgnoreCase)
                        .Select(zone => new ZoneFrequency(zone.Key, zone.Count()))
                        .OrderByDescending(zone => zone.Count)
                        .ThenBy(zone => zone.Zone, StringComparer.OrdinalIgnoreCase)
                ];

                return new GacDefensePatternDetails(
                    group.Key,
                    squad.IsFleet,
                    ToUnitDetails(squad.LeaderDefinitionId, units),
                    [.. squad.MemberDefinitionIds.Select(id => ToUnitDetails(id, units))],
                    archetype?.SquadDefinitionId,
                    archetype?.SquadDefinitionName,
                    archetype?.VariantKey,
                    archetype?.VariantName,
                    appearances,
                    roundsPlaced,
                    Percent(roundsPlaced, rounds.Count),
                    Round(group.Average(item => (decimal)item.Placement.Holds)),
                    Percent(group.Count(item => item.Placement.Holds > 0), appearances),
                    zones,
                    Confidence(rounds.Count, roundsPlaced));
            })
            .OrderByDescending(pattern => pattern.RoundsPlaced)
            .ThenByDescending(pattern => pattern.HoldRate)
            .ThenBy(pattern => pattern.IsFleet)
            .ThenBy(pattern => pattern.Leader.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return observations;
    }

    private static GacCounterPatternDetails[] BuildCounterPatterns(
        IEnumerable<GacHistoricalRound> rounds,
        IReadOnlyDictionary<string, GameUnitDefinition> units)
    {
        return
        [
            .. rounds.SelectMany(round => round.OffenseBattles)
                .GroupBy(
                    battle => $"{CompositionKey(battle.Defender)}>{CompositionKey(battle.Attacker)}",
                    StringComparer.Ordinal)
                .Select(group =>
                {
                    GacOffenseBattle first = group.First();
                    int uses = group.Count();
                    int wins = group.Count(battle => battle.Won);
                    int oneShots = group.Count(battle => battle.Won && battle.Attempt == 1);
                    return new GacCounterPatternDetails(
                        first.Attacker.IsFleet,
                        ToUnitDetails(first.Defender.LeaderDefinitionId, units),
                        ToUnitDetails(first.Attacker.LeaderDefinitionId, units),
                        uses,
                        wins,
                        Percent(wins, uses),
                        oneShots,
                        Percent(oneShots, uses),
                        Round(group.Average(battle => (decimal)battle.Banners)),
                        Round(group.Average(battle => (decimal)battle.Attempt)));
                })
                .OrderByDescending(pattern => pattern.Uses)
                .ThenByDescending(pattern => pattern.WinRate)
                .ThenBy(pattern => pattern.DefenderLeader.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static GacPredictedDefenseDetails[] BuildPredictions(
        IEnumerable<GacDefensePatternDetails> patterns,
        int requiredCount) =>
        [
            .. patterns.Take(requiredCount).Select(pattern => new GacPredictedDefenseDetails(
                pattern.CompositionKey,
                pattern.IsFleet,
                pattern.Leader,
                pattern.Members,
                pattern.PlacementRate,
                pattern.Confidence,
                pattern.SquadDefinitionName,
                pattern.VariantName))
        ];

    private static Dictionary<string, ArchetypeMatch> BuildArchetypeMap(
        IEnumerable<SquadDefinition> definitions)
    {
        var result = new Dictionary<string, ArchetypeMatch>(StringComparer.Ordinal);
        foreach (SquadDefinition definition in definitions)
        {
            foreach (SquadVariant variant in definition.Variants)
            {
                GacHistoricalSquad squad = GacHistoricalSquad.Create(
                    variant.LeaderDefinitionId,
                    variant.MemberDefinitionIds,
                    isFleet: false);
                result.TryAdd(
                    CompositionKey(squad),
                    new ArchetypeMatch(definition.Id, definition.Name, variant.Key, variant.Name));
            }
        }

        return result;
    }

    private static ScoutingUnitDetails ToUnitDetails(
        string definitionId,
        IReadOnlyDictionary<string, GameUnitDefinition> units)
    {
        if (!units.TryGetValue(definitionId, out GameUnitDefinition? definition))
        {
            return new ScoutingUnitDetails(definitionId, definitionId, false);
        }

        bool isGalacticLegend = definition.Tags.Any(tag =>
            tag.Contains("galactic_legend", StringComparison.OrdinalIgnoreCase));
        return new ScoutingUnitDetails(definition.BaseId, definition.Name, isGalacticLegend);
    }

    private static string CompositionKey(GacHistoricalSquad squad)
    {
        string members = string.Join(
            ",",
            squad.MemberDefinitionIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        return $"{(squad.IsFleet ? "fleet" : "squad")}:{squad.LeaderDefinitionId}:{members}";
    }

    private static double? GetFirstAttackDelayMinutes(GacHistoricalRound round)
    {
        DateTimeOffset? firstAttack = round.OffenseBattles
            .Where(battle => battle.AttackedAtUtc.HasValue)
            .Select(battle => battle.AttackedAtUtc)
            .Min();
        if (firstAttack is null)
        {
            return null;
        }

        return (firstAttack.Value - round.StartedAtUtc).TotalMinutes;
    }

    private static string Confidence(int roundsAnalyzed, int roundsPlaced)
    {
        if (roundsAnalyzed >= 6 && roundsPlaced >= 4)
        {
            return "High";
        }

        if (roundsAnalyzed >= 3 && roundsPlaced >= 2)
        {
            return "Medium";
        }

        return "Low";
    }

    private static decimal Percent(int numerator, int denominator) => denominator == 0
        ? 0m
        : Round(numerator * 100m / denominator);

    private static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static SquadFormat ToSquadFormat(GacFormat format) => format switch
    {
        GacFormat.ThreeVsThree => SquadFormat.ThreeVsThree,
        GacFormat.FiveVsFive => SquadFormat.FiveVsFive,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.")
    };

    private static void ValidateAllyCode(long allyCode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(allyCode, 100_000_000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(allyCode, 999_999_999);
    }

    private sealed record DefenseObservation(string RoundId, GacDefensePlacement Placement);

    private sealed record ArchetypeMatch(
        Guid SquadDefinitionId,
        string SquadDefinitionName,
        string VariantKey,
        string VariantName);
}
