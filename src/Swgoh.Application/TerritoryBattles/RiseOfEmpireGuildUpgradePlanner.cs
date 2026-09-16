using System.Globalization;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireGuildUpgradePlanner
{
    public static IReadOnlyCollection<RiseOfEmpireGuildUpgradePriority> Build(
        IReadOnlyCollection<PlayerProfile> players,
        IReadOnlyCollection<RiseOfEmpireOperationPlan> operations,
        IReadOnlyCollection<RiseOfEmpireAnalysis> individualAnalyses,
        GameDataCatalog gameData,
        IReadOnlyCollection<RiseOfEmpireGuildMissionUpgradeCandidate>? missionUpgrades = null,
        IReadOnlyCollection<RiseOfEmpireGuildPhasePlan>? phases = null)
    {
        var priorities = new Dictionary<string, UpgradeAccumulator>(StringComparer.OrdinalIgnoreCase);
        AddOperationGaps(priorities, operations);
        AddBonusUnlockGaps(priorities, players, gameData);
        AddMissionGaps(priorities, missionUpgrades ?? [], phases ?? []);
        AddIndividualPriorities(priorities, individualAnalyses);

        return
        [
            .. priorities.Values
                .Where(value => value.CurrentRelicTier < value.TargetRelicTier)
                .OrderByDescending(value => value.Score)
                .ThenByDescending(value => value.MissionTeamsUnlocked)
                .ThenBy(value => value.ClosestNextStarGap ?? long.MaxValue)
                .ThenBy(value => value.TargetRelicTier - value.CurrentRelicTier)
                .ThenBy(value => value.PlayerName, StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .Select((value, index) => new RiseOfEmpireGuildUpgradePriority(
                    index + 1,
                    value.PlayerAllyCode,
                    value.PlayerName,
                    value.DefinitionId,
                    value.UnitName,
                    value.CurrentRelicTier,
                    value.TargetRelicTier,
                    Math.Max(0, value.TargetRelicTier - value.CurrentRelicTier),
                    Math.Round(value.Score, 1),
                    value.MissionTeamsUnlocked,
                    value.ClosestNextStarGap,
                    [.. value.AffectedPlanets.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)],
                    [.. value.MissionNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)],
                    [.. value.Reasons.OrderBy(reason => reason, StringComparer.OrdinalIgnoreCase)]))
        ];
    }

    private static void AddOperationGaps(
        Dictionary<string, UpgradeAccumulator> priorities,
        IReadOnlyCollection<RiseOfEmpireOperationPlan> operations)
    {
        foreach (RiseOfEmpireOperationPlan operation in operations)
        {
            foreach (RiseOfEmpireOperationMissingSlot slot in operation.Squads
                         .Where(squad => !squad.Complete)
                         .SelectMany(squad => squad.MissingSlots))
            {
                foreach (RiseOfEmpireNearCandidate candidate in slot.NearCandidates
                             .Where(candidate => candidate.CurrentRarity >= slot.RequiredRarity && candidate.RelicsMissing > 0)
                             .Take(3))
                {
                    decimal score = 800m / Math.Max(1, candidate.RelicsMissing);
                    Add(
                        priorities,
                        candidate.PlayerAllyCode,
                        candidate.PlayerName,
                        slot.BaseId,
                        slot.UnitName,
                        candidate.CurrentRelicTier,
                        slot.RequiredRelicTier,
                        score,
                        $"Completa un hueco bloqueante de operación en {operation.PlanetName} (fase {operation.Phase}).",
                        affectedPlanet: operation.PlanetName);
                }
            }
        }
    }

    private static void AddBonusUnlockGaps(
        Dictionary<string, UpgradeAccumulator> priorities,
        IReadOnlyCollection<PlayerProfile> players,
        GameDataCatalog gameData)
    {
        foreach (PlayerProfile player in players)
        {
            AddSpecificUnitIfNeeded(priorities, player, "CEREJUNDA", "Cere Junda", 7, 1_200m, "Acerca al gremio a las 30 victorias necesarias para abrir Zeffo.", "Zeffo");
            AddBestSpecificUnitIfNeeded(
                priorities,
                player,
                ["CALKESTIS", "JEDIKNIGHTCAL"],
                7,
                1_200m,
                "Acerca al gremio a las 30 victorias necesarias para abrir Zeffo.",
                "Zeffo",
                gameData);
            AddSpecificUnitIfNeeded(priorities, player, "BOKATANMANDALORE", "Bo-Katan (Mand'alor)", 7, 1_300m, "Acerca al gremio a las 25 victorias necesarias para abrir Mandalore.", "Mandalore");
            AddSpecificUnitIfNeeded(priorities, player, "THEMANDALORIANBESKARARMOR", "The Mandalorian (Beskar Armor)", 7, 1_300m, "Acerca al gremio a las 25 victorias necesarias para abrir Mandalore.", "Mandalore");
            AddThirdMandalorianIfNeeded(priorities, player, gameData);
        }
    }

    private static void AddMissionGaps(
        Dictionary<string, UpgradeAccumulator> priorities,
        IReadOnlyCollection<RiseOfEmpireGuildMissionUpgradeCandidate> candidates,
        IReadOnlyCollection<RiseOfEmpireGuildPhasePlan> phases)
    {
        Dictionary<string, RiseOfEmpireGuildPlanetPlan> route = phases
            .SelectMany(phase => phase.Planets)
            .ToDictionary(planet => planet.PlanetId, StringComparer.OrdinalIgnoreCase);

        foreach (RiseOfEmpireGuildMissionUpgradeCandidate candidate in candidates)
        {
            route.TryGetValue(candidate.PlanetId, out RiseOfEmpireGuildPlanetPlan? planet);
            long? nextStarGap = planet?.NextStarGap;
            decimal starFactor = StarGapFactor(nextStarGap);
            int relicsMissing = Math.Max(1, candidate.TargetRelicTier - candidate.CurrentRelicTier);
            decimal baseScore = candidate.CompletesTeam ? 2_200m : 850m;
            decimal score = baseScore * starFactor / relicsMissing;
            string impact = candidate.CompletesTeam
                ? $"Completa un equipo concreto para {candidate.MissionName} en {candidate.PlanetName}."
                : $"Deja más cerca el equipo {candidate.TeamName} para {candidate.MissionName} en {candidate.PlanetName}.";
            string starReason = nextStarGap is null
                ? "El planeta no tiene una estrella adicional pendiente en la ruta actual."
                : $"La siguiente estrella de {candidate.PlanetName} tiene un hueco conservador de {FormatCompact(nextStarGap.Value)} puntos.";

            Add(
                priorities,
                candidate.PlayerAllyCode,
                candidate.PlayerName,
                candidate.DefinitionId,
                candidate.UnitName,
                candidate.CurrentRelicTier,
                candidate.TargetRelicTier,
                score,
                $"{impact} {starReason}",
                candidate.PlanetName,
                candidate.MissionName,
                candidate.CompletesTeam ? 1 : 0,
                nextStarGap);
        }
    }

    private static void AddSpecificUnitIfNeeded(
        Dictionary<string, UpgradeAccumulator> priorities,
        PlayerProfile player,
        string definitionId,
        string unitName,
        int targetRelic,
        decimal baseScore,
        string reason,
        string affectedPlanet)
    {
        RosterUnit? unit = player.Roster.FirstOrDefault(unit =>
            string.Equals(unit.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase));
        if (unit is null || unit.Rarity < 7 || unit.RelicTier >= targetRelic)
        {
            return;
        }

        Add(priorities, player.AllyCode, player.Name, definitionId, unitName, unit.RelicTier, targetRelic,
            baseScore / Math.Max(1, targetRelic - unit.RelicTier), reason, affectedPlanet);
    }

    private static void AddBestSpecificUnitIfNeeded(
        Dictionary<string, UpgradeAccumulator> priorities,
        PlayerProfile player,
        IReadOnlyCollection<string> definitionIds,
        int targetRelic,
        decimal baseScore,
        string reason,
        string affectedPlanet,
        GameDataCatalog gameData)
    {
        RosterUnit? unit = player.Roster
            .Where(unit => definitionIds.Any(id => string.Equals(unit.DefinitionId, id, StringComparison.OrdinalIgnoreCase)))
            .Where(unit => unit.Rarity >= 7 && unit.RelicTier < targetRelic)
            .OrderByDescending(unit => unit.RelicTier)
            .ThenByDescending(unit => unit.GalacticPower)
            .FirstOrDefault();
        if (unit is null)
        {
            return;
        }

        Add(priorities, player.AllyCode, player.Name, unit.DefinitionId, UnitName(gameData, unit.DefinitionId),
            unit.RelicTier, targetRelic, baseScore / Math.Max(1, targetRelic - unit.RelicTier), reason, affectedPlanet);
    }

    private static void AddThirdMandalorianIfNeeded(
        Dictionary<string, UpgradeAccumulator> priorities,
        PlayerProfile player,
        GameDataCatalog gameData)
    {
        bool hasReadyThird = player.Roster.Any(unit =>
            !unit.IsShip
            && unit.Rarity >= 7
            && unit.RelicTier >= 7
            && !IsCoreMandalorian(unit.DefinitionId)
            && gameData.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition)
            && MatchesTag(definition, "mandalor"));
        if (hasReadyThird)
        {
            return;
        }

        RosterUnit? candidate = player.Roster
            .Where(unit => !unit.IsShip && unit.Rarity >= 7 && unit.RelicTier < 7 && !IsCoreMandalorian(unit.DefinitionId))
            .Where(unit => gameData.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition) && MatchesTag(definition, "mandalor"))
            .OrderByDescending(unit => unit.RelicTier)
            .ThenByDescending(unit => unit.GalacticPower)
            .FirstOrDefault();
        if (candidate is null)
        {
            return;
        }

        string name = UnitName(gameData, candidate.DefinitionId);
        Add(priorities, player.AllyCode, player.Name, candidate.DefinitionId, name, candidate.RelicTier, 7,
            1_000m / Math.Max(1, 7 - candidate.RelicTier),
            "Tercer Mandaloriano para la misión que abre Mandalore.",
            "Mandalore");
    }

    private static void AddIndividualPriorities(
        Dictionary<string, UpgradeAccumulator> priorities,
        IReadOnlyCollection<RiseOfEmpireAnalysis> analyses)
    {
        foreach (RiseOfEmpireAnalysis analysis in analyses)
        {
            foreach (RiseOfEmpireUpgradePriority priority in analysis.UpgradePriorities.Take(8))
            {
                Add(
                    priorities,
                    analysis.AllyCode,
                    analysis.PlayerName,
                    priority.DefinitionId,
                    priority.Name,
                    priority.CurrentRelicTier,
                    priority.TargetRelicTier,
                    priority.Score * 0.2m,
                    $"Mejora equipos de combate personales: {string.Join(", ", priority.Planets)}.");
            }
        }
    }

    private static void Add(
        Dictionary<string, UpgradeAccumulator> priorities,
        long allyCode,
        string playerName,
        string definitionId,
        string unitName,
        int currentRelic,
        int targetRelic,
        decimal score,
        string reason,
        string? affectedPlanet = null,
        string? missionName = null,
        int missionTeamsUnlocked = 0,
        long? nextStarGap = null)
    {
        string key = $"{allyCode}:{definitionId}";
        if (!priorities.TryGetValue(key, out UpgradeAccumulator? value))
        {
            value = new UpgradeAccumulator(allyCode, playerName, definitionId, unitName, currentRelic, targetRelic);
            priorities[key] = value;
        }

        value.TargetRelicTier = Math.Max(value.TargetRelicTier, targetRelic);
        value.Score += score;
        value.MissionTeamsUnlocked += missionTeamsUnlocked;
        if (nextStarGap is not null)
        {
            value.ClosestNextStarGap = value.ClosestNextStarGap is null
                ? nextStarGap
                : Math.Min(value.ClosestNextStarGap.Value, nextStarGap.Value);
        }

        if (!string.IsNullOrWhiteSpace(affectedPlanet))
        {
            value.AffectedPlanets.Add(affectedPlanet);
        }

        if (!string.IsNullOrWhiteSpace(missionName))
        {
            value.MissionNames.Add(missionName);
        }

        value.Reasons.Add(reason);
    }

    private static decimal StarGapFactor(long? gap) => gap switch
    {
        null => 0.8m,
        <= 25_000_000 => 2.5m,
        <= 75_000_000 => 2m,
        <= 150_000_000 => 1.5m,
        <= 300_000_000 => 1.2m,
        _ => 1m
    };

    private static string FormatCompact(long value) => value >= 1_000_000_000
        ? $"{value / 1_000_000_000d:0.##}B"
        : value >= 1_000_000
            ? $"{value / 1_000_000d:0.#}M"
            : $"{value / 1_000d:0.#}K";

    private static bool IsCoreMandalorian(string definitionId) =>
        string.Equals(definitionId, "BOKATANMANDALORE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(definitionId, "THEMANDALORIANBESKARARMOR", StringComparison.OrdinalIgnoreCase);

    private static string UnitName(GameDataCatalog gameData, string definitionId) =>
        gameData.Units.TryGetValue(definitionId, out GameUnitDefinition? definition)
            ? definition.Name
            : definitionId;

    private static bool MatchesTag(GameUnitDefinition definition, string keyword)
    {
        string normalized = Normalize(keyword);
        return definition.Tags.Any(tag => Normalize(tag).Contains(normalized, StringComparison.Ordinal))
            || definition.Factions.Any(faction => Normalize(faction).Contains(normalized, StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private sealed class UpgradeAccumulator(
        long playerAllyCode,
        string playerName,
        string definitionId,
        string unitName,
        int currentRelicTier,
        int targetRelicTier)
    {
        public long PlayerAllyCode { get; } = playerAllyCode;
        public string PlayerName { get; } = playerName;
        public string DefinitionId { get; } = definitionId;
        public string UnitName { get; } = unitName;
        public int CurrentRelicTier { get; } = currentRelicTier;
        public int TargetRelicTier { get; set; } = targetRelicTier;
        public decimal Score { get; set; }
        public int MissionTeamsUnlocked { get; set; }
        public long? ClosestNextStarGap { get; set; }
        public HashSet<string> AffectedPlanets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MissionNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Reasons { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
