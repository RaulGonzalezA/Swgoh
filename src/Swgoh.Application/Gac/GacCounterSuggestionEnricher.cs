using Swgoh.Application.Players;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal static class GacCounterSuggestionEnricher
{
    public static CurrentGacBattlePlan Enrich(
        CurrentGacBattlePlan plan,
        GacFormat format,
        IReadOnlyCollection<GacCounterStatistics> statistics,
        IReadOnlyCollection<PlayerRosterUnit> playerCharacters,
        IReadOnlyCollection<PlayerRosterUnit> playerShips)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(playerCharacters);
        ArgumentNullException.ThrowIfNull(playerShips);

        if (statistics.Count == 0 || plan.CounterSuggestions.Count == 0)
        {
            return plan;
        }

        Dictionary<string, PlayerRosterUnit> characters = ToUnitMap(playerCharacters);
        Dictionary<string, PlayerRosterUnit> ships = ToUnitMap(playerShips);
        GacBattleCounterSuggestion[] suggestions =
        [
            .. plan.CounterSuggestions.Select(suggestion => EnrichSuggestion(
                suggestion,
                format,
                statistics,
                suggestion.Threat.IsShip ? ships : characters))
        ];

        return plan with { CounterSuggestions = suggestions };
    }

    private static GacBattleCounterSuggestion EnrichSuggestion(
        GacBattleCounterSuggestion suggestion,
        GacFormat format,
        IReadOnlyCollection<GacCounterStatistics> statistics,
        IReadOnlyDictionary<string, PlayerRosterUnit> ownedUnits)
    {
        GacCounterStatistics? best = statistics
            .Where(item => item.IsFleet == suggestion.Threat.IsShip)
            .Where(item => item.DefenderLeaderDefinitionId.Equals(
                suggestion.Threat.DefinitionId,
                StringComparison.OrdinalIgnoreCase))
            .Where(item => IsUsableTeam(item, format, ownedUnits))
            .OrderByDescending(item => item.Uses)
            .ThenByDescending(item => item.WinRate)
            .ThenByDescending(item => item.OneShotRate)
            .ThenByDescending(item => item.AverageBanners)
            .FirstOrDefault();
        if (best is null)
        {
            return suggestion;
        }

        GacBattleUnit[] team =
        [
            ToBattleUnit(ownedUnits[best.AttackerLeaderDefinitionId]),
            .. best.AttackerMemberDefinitionIds.Select(id => ToBattleUnit(ownedUnits[id]))
        ];
        string confidence = Confidence(best);
        string rationale =
            $"Observed {best.Uses} battle(s) across {best.PlayersObserved} player(s): " +
            $"{best.WinRate:0.#}% win rate, {best.OneShotRate:0.#}% one-shot rate and " +
            $"{best.AverageBanners:0.#} average banners.";

        return new GacBattleCounterSuggestion(
            suggestion.Threat,
            team,
            confidence,
            "GlobalHistoricalCounterData",
            rationale,
            RequiresDatacronVerification: true,
            RecommendedTeam: team,
            Uses: best.Uses,
            WinRate: best.WinRate,
            OneShotRate: best.OneShotRate,
            AverageBanners: best.AverageBanners,
            PlayersObserved: best.PlayersObserved);
    }

    private static bool IsUsableTeam(
        GacCounterStatistics statistics,
        GacFormat format,
        IReadOnlyDictionary<string, PlayerRosterUnit> ownedUnits)
    {
        if (!ownedUnits.ContainsKey(statistics.AttackerLeaderDefinitionId)
            || statistics.AttackerMemberDefinitionIds.Any(id => !ownedUnits.ContainsKey(id)))
        {
            return false;
        }

        if (statistics.IsFleet)
        {
            return true;
        }

        int expectedMembers = format == GacFormat.ThreeVsThree ? 2 : 4;
        return statistics.AttackerMemberDefinitionIds.Count == expectedMembers;
    }

    private static string Confidence(GacCounterStatistics statistics)
    {
        if (statistics.Uses >= 10 && statistics.PlayersObserved >= 5 && statistics.WinRate >= 70m)
        {
            return "High";
        }

        if (statistics.Uses >= 3 && statistics.PlayersObserved >= 2 && statistics.WinRate >= 55m)
        {
            return "Medium";
        }

        return "Low";
    }

    private static Dictionary<string, PlayerRosterUnit> ToUnitMap(IEnumerable<PlayerRosterUnit> units) => units
        .GroupBy(unit => unit.DefinitionId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group.OrderByDescending(unit => unit.GalacticPower).First(),
            StringComparer.OrdinalIgnoreCase);

    private static GacBattleUnit ToBattleUnit(PlayerRosterUnit unit) => new(
        unit.DefinitionId,
        unit.Name,
        unit.GalacticPower,
        unit.RelicTier,
        unit.ZetaCount,
        unit.OmicronCount,
        unit.IsShip,
        unit.Tags.Any(tag => tag.Contains("galactic_legend", StringComparison.OrdinalIgnoreCase)));
}
