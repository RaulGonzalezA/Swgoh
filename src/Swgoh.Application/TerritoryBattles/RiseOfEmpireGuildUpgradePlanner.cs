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
        GameDataCatalog gameData)
    {
        var priorities = new Dictionary<string, UpgradeAccumulator>(StringComparer.OrdinalIgnoreCase);
        AddOperationGaps(priorities, operations);
        AddBonusUnlockGaps(priorities, players, gameData);
        AddIndividualPriorities(priorities, individualAnalyses);

        return
        [
            .. priorities.Values
                .Where(value => value.CurrentRelicTier < value.TargetRelicTier)
                .OrderByDescending(value => value.Score)
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
                        $"Completa un hueco bloqueante de operación en {operation.PlanetName} (fase {operation.Phase}).");
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
            AddSpecificUnitIfNeeded(priorities, player, "CEREJUNDA", "Cere Junda", 7, 1_200m, "Acerca al gremio a las 30 victorias necesarias para abrir Zeffo.");
            AddBestSpecificUnitIfNeeded(
                priorities,
                player,
                ["CALKESTIS", "JEDIKNIGHTCAL"],
                7,
                1_200m,
                "Acerca al gremio a las 30 victorias necesarias para abrir Zeffo.");
            AddSpecificUnitIfNeeded(priorities, player, "BOKATANMANDALORE", "Bo-Katan (Mand'alor)", 7, 1_300m, "Acerca al gremio a las 25 victorias necesarias para abrir Mandalore.");
            AddSpecificUnitIfNeeded(priorities, player, "THEMANDALORIANBESKARARMOR", "The Mandalorian (Beskar Armor)", 7, 1_300m, "Acerca al gremio a las 25 victorias necesarias para abrir Mandalore.");
            AddThirdMandalorianIfNeeded(priorities, player, gameData);
        }
    }

    private static void AddSpecificUnitIfNeeded(
        Dictionary<string, UpgradeAccumulator> priorities,
        PlayerProfile player,
        string definitionId,
        string unitName,
        int targetRelic,
        decimal baseScore,
        string reason)
    {
        RosterUnit? unit = player.Roster.FirstOrDefault(unit =>
            string.Equals(unit.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase));
        if (unit is null || unit.Rarity < 7 || unit.RelicTier >= targetRelic)
        {
            return;
        }

        Add(priorities, player.AllyCode, player.Name, definitionId, unitName, unit.RelicTier, targetRelic,
            baseScore / Math.Max(1, targetRelic - unit.RelicTier), reason);
    }

    private static void AddBestSpecificUnitIfNeeded(
        Dictionary<string, UpgradeAccumulator> priorities,
        PlayerProfile player,
        IReadOnlyCollection<string> definitionIds,
        int targetRelic,
        decimal baseScore,
        string reason)
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

        Add(priorities, player.AllyCode, player.Name, unit.DefinitionId, UnitName(gameData: null, unit.DefinitionId),
            unit.RelicTier, targetRelic, baseScore / Math.Max(1, targetRelic - unit.RelicTier), reason);
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

        string name = gameData.Units.TryGetValue(candidate.DefinitionId, out GameUnitDefinition? definition)
            ? definition.Name
            : candidate.DefinitionId;
        Add(priorities, player.AllyCode, player.Name, candidate.DefinitionId, name, candidate.RelicTier, 7,
            1_000m / Math.Max(1, 7 - candidate.RelicTier),
            "Tercer Mandaloriano para la misión que abre Mandalore.");
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
        string reason)
    {
        string key = $"{allyCode}:{definitionId}";
        if (!priorities.TryGetValue(key, out UpgradeAccumulator? value))
        {
            value = new UpgradeAccumulator(allyCode, playerName, definitionId, unitName, currentRelic, targetRelic);
            priorities[key] = value;
        }

        value.TargetRelicTier = Math.Min(value.TargetRelicTier, targetRelic);
        value.Score += score;
        value.Reasons.Add(reason);
    }

    private static bool IsCoreMandalorian(string definitionId) =>
        string.Equals(definitionId, "BOKATANMANDALORE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(definitionId, "THEMANDALORIANBESKARARMOR", StringComparison.OrdinalIgnoreCase);

    private static string UnitName(GameDataCatalog? gameData, string definitionId) =>
        gameData is not null && gameData.Units.TryGetValue(definitionId, out GameUnitDefinition? definition)
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
        public HashSet<string> Reasons { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
