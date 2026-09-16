using System.Globalization;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireBonusUnlockAnalyzer
{
    private const int RequiredRelicTier = 7;

    public static IReadOnlyCollection<RiseOfEmpireBonusUnlockReadiness> Analyze(
        IReadOnlyCollection<PlayerProfile> players,
        GameDataCatalog gameData) =>
        [
            AnalyzeZeffo(players),
            AnalyzeMandalore(players, gameData)
        ];

    private static RiseOfEmpireBonusUnlockReadiness AnalyzeZeffo(IReadOnlyCollection<PlayerProfile> players)
    {
        RiseOfEmpireGuildMemberReadiness[] members =
        [
            .. players.Select(player =>
            {
                var missing = new List<string>();
                RosterUnit? cere = FindUnit(player, "CEREJUNDA");
                RosterUnit? cal = BestUnit(player, "CALKESTIS", "JEDIKNIGHTCAL");
                AddUnitRequirement(missing, cere, "Cere Junda");
                AddUnitRequirement(missing, cal, "Cal Kestis o Jedi Knight Cal Kestis");
                return new RiseOfEmpireGuildMemberReadiness(player.AllyCode, player.Name, missing.Count == 0, missing);
            })
        ];
        return Build("Zeffo", "Bracca", requiredClears: 30, members);
    }

    private static RiseOfEmpireBonusUnlockReadiness AnalyzeMandalore(
        IReadOnlyCollection<PlayerProfile> players,
        GameDataCatalog gameData)
    {
        RiseOfEmpireGuildMemberReadiness[] members =
        [
            .. players.Select(player =>
            {
                var missing = new List<string>();
                RosterUnit? bo = FindUnit(player, "BOKATANMANDALORE");
                RosterUnit? bam = FindUnit(player, "THEMANDALORIANBESKARARMOR");
                AddUnitRequirement(missing, bo, "Bo-Katan (Mand'alor)");
                AddUnitRequirement(missing, bam, "The Mandalorian (Beskar Armor)");

                bool thirdMandalorian = player.Roster.Any(unit =>
                    !unit.IsShip
                    && !string.Equals(unit.DefinitionId, "BOKATANMANDALORE", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(unit.DefinitionId, "THEMANDALORIANBESKARARMOR", StringComparison.OrdinalIgnoreCase)
                    && IsReady(unit)
                    && gameData.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition)
                    && MatchesTag(definition, "mandalor"));
                if (!thirdMandalorian)
                {
                    missing.Add("otro Mandaloriano R7+");
                }

                return new RiseOfEmpireGuildMemberReadiness(player.AllyCode, player.Name, missing.Count == 0, missing);
            })
        ];
        return Build("Mandalore", "Tatooine", requiredClears: 25, members);
    }

    private static RiseOfEmpireBonusUnlockReadiness Build(
        string planetName,
        string sourcePlanet,
        int requiredClears,
        IReadOnlyCollection<RiseOfEmpireGuildMemberReadiness> members)
    {
        RiseOfEmpireGuildMemberReadiness[] eligible = [.. members.Where(member => member.Ready)];
        RiseOfEmpireGuildMemberReadiness[] closest =
        [
            .. members.Where(member => !member.Ready)
                .OrderBy(member => member.MissingRequirements.Count)
                .ThenBy(member => member.PlayerName, StringComparer.OrdinalIgnoreCase)
                .Take(8)
        ];
        return new RiseOfEmpireBonusUnlockReadiness(
            planetName,
            sourcePlanet,
            requiredClears,
            eligible.Length,
            eligible.Length >= requiredClears,
            eligible,
            closest);
    }

    private static RosterUnit? FindUnit(PlayerProfile player, string definitionId) => player.Roster
        .Where(unit => string.Equals(unit.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(unit => unit.RelicTier)
        .FirstOrDefault();

    private static RosterUnit? BestUnit(PlayerProfile player, params string[] definitionIds) => player.Roster
        .Where(unit => definitionIds.Any(id => string.Equals(unit.DefinitionId, id, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(unit => unit.RelicTier)
        .ThenByDescending(unit => unit.GalacticPower)
        .FirstOrDefault();

    private static void AddUnitRequirement(List<string> missing, RosterUnit? unit, string label)
    {
        if (unit is null)
        {
            missing.Add($"{label}: no disponible");
        }
        else if (!IsReady(unit))
        {
            missing.Add($"{label}: R{unit.RelicTier} → R{RequiredRelicTier}");
        }
    }

    private static bool IsReady(RosterUnit unit) => unit.Rarity >= 7 && unit.RelicTier >= RequiredRelicTier;

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
}
