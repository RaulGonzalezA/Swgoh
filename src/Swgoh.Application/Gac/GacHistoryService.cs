using Swgoh.Application.GameData;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class GacHistoryService(
    IGacHistoryRepository repository,
    ISwgohGameDataCatalog gameDataCatalog) : IGacHistoryService
{
    private const int MaxImportRounds = 100;
    private const int MaxQueryRounds = 200;

    public async Task<GacHistoryImportResult> ImportAsync(
        long allyCode,
        IReadOnlyCollection<GacHistoryRoundInput> rounds,
        CancellationToken cancellationToken = default)
    {
        ValidateAllyCode(allyCode);
        ArgumentNullException.ThrowIfNull(rounds);
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds.Count, 1, nameof(rounds));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rounds.Count, MaxImportRounds, nameof(rounds));

        GameDataCatalog catalog = await gameDataCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, GameUnitDefinition> units = catalog.Units.Values
            .GroupBy(unit => unit.BaseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        GacHistoricalRound[] normalized =
        [
            .. rounds.Select(round => MapRound(allyCode, round, units))
        ];

        if (normalized.Select(round => round.Id).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new ArgumentException("Import contains duplicate GAC rounds.", nameof(rounds));
        }

        await repository.UpsertManyAsync(normalized, cancellationToken).ConfigureAwait(false);
        return new GacHistoryImportResult(normalized.Length);
    }

    public Task<IReadOnlyCollection<GacHistoricalRound>> GetAsync(
        long allyCode,
        GacHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateAllyCode(allyCode);
        ArgumentNullException.ThrowIfNull(query);
        int maxRounds = Math.Clamp(query.MaxRounds, 1, MaxQueryRounds);
        return repository.GetAsync(allyCode, query.Format, maxRounds, cancellationToken);
    }

    private static GacHistoricalRound MapRound(
        long allyCode,
        GacHistoryRoundInput input,
        IReadOnlyDictionary<string, GameUnitDefinition> units)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Defenses);
        ArgumentNullException.ThrowIfNull(input.OffenseBattles);

        GacDefensePlacement[] defenses =
        [
            .. input.Defenses.Select(defense => GacDefensePlacement.Create(
                defense.Zone,
                MapSquad(defense.Squad, units),
                defense.Holds,
                defense.Defeated))
        ];

        GacOffenseBattle[] offenseBattles =
        [
            .. input.OffenseBattles.Select(battle => GacOffenseBattle.Create(
                battle.Zone,
                MapSquad(battle.Defender, units),
                MapSquad(battle.Attacker, units),
                battle.Won,
                battle.Banners,
                battle.Attempt,
                battle.AttackedAtUtc))
        ];

        return GacHistoricalRound.Create(
            allyCode,
            input.Season,
            input.EventNumber,
            input.RoundNumber,
            input.Format,
            input.League,
            input.StartedAtUtc,
            input.FullClear,
            input.Source,
            defenses,
            offenseBattles);
    }

    private static GacHistoricalSquad MapSquad(
        GacHistorySquadInput input,
        IReadOnlyDictionary<string, GameUnitDefinition> units)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.MemberDefinitionIds);

        string leader = CanonicalizeUnit(input.LeaderDefinitionId, input.IsFleet, units);
        string[] members =
        [
            .. input.MemberDefinitionIds.Select(member => CanonicalizeUnit(member, input.IsFleet, units))
        ];
        return GacHistoricalSquad.Create(leader, members, input.IsFleet);
    }

    private static string CanonicalizeUnit(
        string definitionId,
        bool expectedShip,
        IReadOnlyDictionary<string, GameUnitDefinition> units)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        string requested = definitionId.Trim();
        if (!units.TryGetValue(requested, out GameUnitDefinition? definition))
        {
            throw new ArgumentException($"Unknown Game Data unit '{requested}'.", nameof(definitionId));
        }

        if (definition.IsShip != expectedShip)
        {
            string expected = expectedShip ? "ship" : "character";
            throw new ArgumentException(
                $"Unit '{definition.BaseId}' is not a {expected} and cannot be used in this historical squad.",
                nameof(definitionId));
        }

        return definition.BaseId;
    }

    private static void ValidateAllyCode(long allyCode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(allyCode, 100_000_000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(allyCode, 999_999_999);
    }
}
