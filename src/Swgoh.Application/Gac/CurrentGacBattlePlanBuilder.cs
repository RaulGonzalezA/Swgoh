using Swgoh.Application.Players;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal static class CurrentGacBattlePlanBuilder
{
    private const int ThreatLimit = 14;
    private const int CounterThreatLimit = 10;
    private const int CharacterReserveLimit3V3 = 10;
    private const int CharacterReserveLimit5V5 = 8;
    private const int FleetReserveLimit = 5;

    public static CurrentGacBattlePlan Build(
        CurrentGacOpponent opponent,
        PlayerRosterAnalysis playerAnalysis,
        IReadOnlyCollection<PlayerRosterUnit> playerCharacters,
        IReadOnlyCollection<PlayerRosterUnit> playerShips,
        IReadOnlyCollection<PlayerRosterUnit> playerOmicrons,
        CurrentOpponentRosterScouting opponentRoster,
        OpponentScoutingReport? historicalScouting)
    {
        ArgumentNullException.ThrowIfNull(opponent);
        ArgumentNullException.ThrowIfNull(playerAnalysis);
        ArgumentNullException.ThrowIfNull(playerCharacters);
        ArgumentNullException.ThrowIfNull(playerShips);
        ArgumentNullException.ThrowIfNull(playerOmicrons);
        ArgumentNullException.ThrowIfNull(opponentRoster);

        PlayerRosterUnit[] opponentCharacters = DistinctUnits(
            opponentRoster.GalacticLegends
                .Concat(opponentRoster.OmicronCharacters)
                .Concat(opponentRoster.TopCharacters));
        PlayerRosterUnit[] opponentShips = DistinctUnits(opponentRoster.TopShips);
        PlayerRosterUnit[] ownCharacters = DistinctUnits(playerCharacters.Concat(playerOmicrons));
        PlayerRosterUnit[] ownShips = DistinctUnits(playerShips);

        GacBattleRosterComparison comparison = new(
            playerAnalysis.GalacticPower,
            opponentRoster.Analysis.GalacticPower,
            playerAnalysis.GalacticPower - opponentRoster.Analysis.GalacticPower,
            CountGalacticLegends(ownCharacters),
            opponentRoster.GalacticLegends.Count,
            ownCharacters.Count(unit => unit.OmicronCount > 0),
            opponentRoster.OmicronCharacters.Count,
            playerAnalysis.Relic7Plus,
            opponentRoster.Analysis.Relic7Plus,
            playerAnalysis.Relic9Plus,
            opponentRoster.Analysis.Relic9Plus);

        GacBattleThreat[] threats = BuildThreats(opponentCharacters, opponentShips);
        GacBattleDefensePrediction[] defensePredictions = BuildDefensePredictions(
            threats,
            historicalScouting);
        GacBattleAttackReserve[] attackReserves = BuildAttackReserves(
            opponent.Format,
            ownCharacters,
            ownShips,
            threats);
        GacBattleCounterSuggestion[] counterSuggestions = BuildCounterSuggestions(
            threats,
            ownCharacters,
            ownShips,
            historicalScouting);

        string[] warnings = BuildWarnings(historicalScouting);
        return new CurrentGacBattlePlan(
            comparison,
            threats,
            defensePredictions,
            attackReserves,
            counterSuggestions,
            warnings);
    }

    private static GacBattleThreat[] BuildThreats(
        IReadOnlyCollection<PlayerRosterUnit> characters,
        IReadOnlyCollection<PlayerRosterUnit> ships) =>
    [
        .. characters.Concat(ships)
            .Select(unit => new GacBattleThreat(
                ToUnit(unit),
                ThreatScore(unit),
                Priority(ThreatScore(unit)),
                ThreatCategory(unit),
                ThreatReason(unit)))
            .OrderByDescending(threat => threat.Score)
            .ThenByDescending(threat => threat.Unit.GalacticPower)
            .Take(ThreatLimit)
    ];

    private static GacBattleDefensePrediction[] BuildDefensePredictions(
        IReadOnlyCollection<GacBattleThreat> threats,
        OpponentScoutingReport? historicalScouting)
    {
        if (historicalScouting is not null
            && (historicalScouting.PredictedSquadDefenses.Count > 0
                || historicalScouting.PredictedFleetDefenses.Count > 0))
        {
            return
            [
                .. historicalScouting.PredictedSquadDefenses
                    .Concat(historicalScouting.PredictedFleetDefenses)
                    .Select(prediction => new GacBattleDefensePrediction(
                        prediction.Leader.Name,
                        prediction.Leader.DefinitionId,
                        [.. prediction.Members.Select(member => member.Name)],
                        prediction.IsFleet,
                        prediction.Probability,
                        prediction.Confidence,
                        "HistoricalPattern",
                        prediction.SquadDefinitionName,
                        prediction.VariantName))
            ];
        }

        return
        [
            .. threats
                .Where(threat => threat.Priority is "Critical" or "High")
                .Take(8)
                .Select(threat => new GacBattleDefensePrediction(
                    threat.Unit.Name,
                    threat.Unit.DefinitionId,
                    [],
                    threat.Unit.IsShip,
                    null,
                    "Low",
                    "RosterThreatHeuristic",
                    null,
                    null))
        ];
    }

    private static GacBattleAttackReserve[] BuildAttackReserves(
        GacFormat format,
        IReadOnlyCollection<PlayerRosterUnit> characters,
        IReadOnlyCollection<PlayerRosterUnit> ships,
        IReadOnlyCollection<GacBattleThreat> opponentThreats)
    {
        int characterLimit = format == GacFormat.ThreeVsThree
            ? CharacterReserveLimit3V3
            : CharacterReserveLimit5V5;
        int criticalThreats = opponentThreats.Count(threat => threat.Priority == "Critical");

        IEnumerable<PlayerRosterUnit> characterCandidates = characters
            .OrderByDescending(ReserveScore)
            .ThenByDescending(unit => unit.GalacticPower)
            .Take(Math.Max(characterLimit, Math.Min(characterLimit + 2, criticalThreats + 4)));
        IEnumerable<PlayerRosterUnit> shipCandidates = ships
            .OrderByDescending(ReserveScore)
            .ThenByDescending(unit => unit.GalacticPower)
            .Take(FleetReserveLimit);

        return
        [
            .. characterCandidates.Concat(shipCandidates)
                .Select(unit => new GacBattleAttackReserve(
                    ToUnit(unit),
                    ReserveRole(unit),
                    Priority(ThreatScore(unit)),
                    ReserveReason(unit, criticalThreats)))
        ];
    }

    private static GacBattleCounterSuggestion[] BuildCounterSuggestions(
        IReadOnlyCollection<GacBattleThreat> threats,
        IReadOnlyCollection<PlayerRosterUnit> ownCharacters,
        IReadOnlyCollection<PlayerRosterUnit> ownShips,
        OpponentScoutingReport? historicalScouting)
    {
        var suggestions = new List<GacBattleCounterSuggestion>();
        foreach (GacBattleThreat threat in threats.Take(CounterThreatLimit))
        {
            GacBattleCounterSuggestion? historical = FindHistoricalCounter(
                threat,
                ownCharacters,
                ownShips,
                historicalScouting);
            if (historical is not null)
            {
                suggestions.Add(historical);
                continue;
            }

            IReadOnlyCollection<PlayerRosterUnit> pool = threat.Unit.IsShip ? ownShips : ownCharacters;
            PlayerRosterUnit[] candidates = CandidateCounterAnchors(threat, pool);
            if (candidates.Length == 0)
            {
                continue;
            }

            suggestions.Add(new GacBattleCounterSuggestion(
                threat.Unit,
                [.. candidates.Select(ToUnit)],
                "Low",
                "RosterStrengthHeuristic",
                threat.Unit.IsGalacticLegend
                    ? "No observed counter history is available. These are your strongest GL-grade anchors, not a guaranteed matchup."
                    : "No observed counter history is available. These are strong available anchors from your roster, not a complete guaranteed team.",
                true));
        }

        return [.. suggestions];
    }

    private static GacBattleCounterSuggestion? FindHistoricalCounter(
        GacBattleThreat threat,
        IReadOnlyCollection<PlayerRosterUnit> ownCharacters,
        IReadOnlyCollection<PlayerRosterUnit> ownShips,
        OpponentScoutingReport? historicalScouting)
    {
        if (historicalScouting is null)
        {
            return null;
        }

        IReadOnlyCollection<PlayerRosterUnit> ownPool = threat.Unit.IsShip ? ownShips : ownCharacters;
        Dictionary<string, PlayerRosterUnit> ownByDefinition = ownPool
            .GroupBy(unit => unit.DefinitionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        GacCounterPatternDetails? pattern = historicalScouting.CounterPatterns
            .Where(candidate => candidate.IsFleet == threat.Unit.IsShip)
            .Where(candidate => candidate.DefenderLeader.DefinitionId.Equals(
                threat.Unit.DefinitionId,
                StringComparison.OrdinalIgnoreCase))
            .Where(candidate => ownByDefinition.ContainsKey(candidate.AttackerLeader.DefinitionId))
            .OrderByDescending(candidate => candidate.Uses)
            .ThenByDescending(candidate => candidate.WinRate)
            .FirstOrDefault();
        if (pattern is null)
        {
            return null;
        }

        PlayerRosterUnit attacker = ownByDefinition[pattern.AttackerLeader.DefinitionId];
        string confidence = pattern.Uses >= 5 && pattern.WinRate >= 70m
            ? "High"
            : pattern.Uses >= 2
                ? "Medium"
                : "Low";
        return new GacBattleCounterSuggestion(
            threat.Unit,
            [ToUnit(attacker)],
            confidence,
            "HistoricalCounterPattern",
            $"Observed {pattern.Uses} time(s): {pattern.WinRate:0.#}% win rate, {pattern.OneShotRate:0.#}% one-shot rate, {pattern.AverageBanners:0.#} average banners.",
            true);
    }

    private static PlayerRosterUnit[] CandidateCounterAnchors(
        GacBattleThreat threat,
        IEnumerable<PlayerRosterUnit> pool)
    {
        IEnumerable<PlayerRosterUnit> ordered = threat.Unit.IsShip
            ? pool.OrderByDescending(unit => unit.GalacticPower)
            : threat.Unit.IsGalacticLegend
                ? pool.OrderByDescending(IsGalacticLegend)
                    .ThenByDescending(unit => unit.RelicTier)
                    .ThenByDescending(unit => unit.GalacticPower)
                : pool.OrderByDescending(unit => unit.OmicronCount > 0)
                    .ThenByDescending(IsGalacticLegend)
                    .ThenByDescending(unit => unit.RelicTier)
                    .ThenByDescending(unit => unit.GalacticPower);

        return
        [
            .. ordered
                .Where(unit => unit.GalacticPower > 0)
                .Take(3)
        ];
    }

    private static string[] BuildWarnings(OpponentScoutingReport? historicalScouting)
    {
        var warnings = new List<string>
        {
            "Datacrons, mods, exact three-unit/five-unit compositions and temporary event modifiers are not modeled; verify the final matchup before attacking."
        };
        if (historicalScouting is null || historicalScouting.RoundsAnalyzed == 0)
        {
            warnings.Add("No historical GAC rounds are available for this opponent yet; defense predictions and fallback counters are roster-based heuristics.");
        }

        warnings.Add("Counter suggestions marked RosterStrengthHeuristic are candidate anchors, not guaranteed complete counter teams.");
        return [.. warnings];
    }

    private static GacBattleUnit ToUnit(PlayerRosterUnit unit) => new(
        unit.DefinitionId,
        unit.Name,
        unit.GalacticPower,
        unit.RelicTier,
        unit.ZetaCount,
        unit.OmicronCount,
        unit.IsShip,
        IsGalacticLegend(unit));

    private static int ThreatScore(PlayerRosterUnit unit)
    {
        int score = (int)Math.Min(80, unit.GalacticPower / 1_000);
        if (IsGalacticLegend(unit))
        {
            score += 120;
        }

        if (unit.IsShip)
        {
            score += unit.Rarity * 3;
            return score;
        }

        score += unit.RelicTier * 6;
        score += unit.OmicronCount * 25;
        score += Math.Min(18, unit.ZetaCount * 3);
        return score;
    }

    private static int ReserveScore(PlayerRosterUnit unit) =>
        ThreatScore(unit) + (unit.OmicronCount > 0 ? 20 : 0);

    private static string Priority(int score) => score switch
    {
        >= 220 => "Critical",
        >= 155 => "High",
        >= 105 => "Medium",
        _ => "Watch"
    };

    private static string ThreatCategory(PlayerRosterUnit unit)
    {
        if (unit.IsShip)
        {
            return "Fleet";
        }

        if (IsGalacticLegend(unit))
        {
            return "GalacticLegend";
        }

        return unit.OmicronCount > 0 ? "Omicron" : "HighPower";
    }

    private static string ThreatReason(PlayerRosterUnit unit)
    {
        if (unit.IsShip)
        {
            return $"Top fleet unit at {unit.GalacticPower:N0} GP.";
        }

        if (IsGalacticLegend(unit))
        {
            return $"Galactic Legend at R{unit.RelicTier} with {unit.OmicronCount} omicron(s).";
        }

        if (unit.OmicronCount > 0)
        {
            return $"GAC-relevant investment: {unit.OmicronCount} omicron(s), R{unit.RelicTier}, {unit.GalacticPower:N0} GP.";
        }

        return $"High roster investment: R{unit.RelicTier}, {unit.GalacticPower:N0} GP.";
    }

    private static string ReserveRole(PlayerRosterUnit unit)
    {
        if (unit.IsShip)
        {
            return "FleetAnchor";
        }

        if (IsGalacticLegend(unit))
        {
            return "GlAnswer";
        }

        return unit.OmicronCount > 0 ? "GacSpecialist" : "HighPowerFlex";
    }

    private static string ReserveReason(PlayerRosterUnit unit, int criticalThreats) =>
        unit.IsShip
            ? "Keep a high-end fleet anchor available until the opponent fleet zones are known."
            : IsGalacticLegend(unit)
                ? $"Preserve GL-grade flexibility while the opponent shows {criticalThreats} critical roster threat(s)."
                : unit.OmicronCount > 0
                    ? "Keep this GAC specialist available until its best matchup is visible."
                    : "Strong flexible unit to avoid committing early into a low-value matchup.";

    private static int CountGalacticLegends(IEnumerable<PlayerRosterUnit> units) =>
        units.Count(IsGalacticLegend);

    private static bool IsGalacticLegend(PlayerRosterUnit unit) =>
        unit.Tags.Any(tag => tag.Contains("galactic_legend", StringComparison.OrdinalIgnoreCase));

    private static PlayerRosterUnit[] DistinctUnits(IEnumerable<PlayerRosterUnit> units) =>
    [
        .. units.GroupBy(unit => unit.DefinitionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.GalacticPower).First())
    ];
}
