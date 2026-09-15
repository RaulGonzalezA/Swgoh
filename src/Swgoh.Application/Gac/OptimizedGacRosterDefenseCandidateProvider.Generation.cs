using System.Security.Cryptography;
using System.Text;

using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Squads;

namespace Swgoh.Application.Gac;

internal sealed partial class OptimizedGacRosterDefenseCandidateProvider
{
    private const int MaxCuratedCandidates = 64;
    private const int MaxHeuristicLeaders = 24;
    private const int MaxFleetMembers = 7;
    private const decimal CuratedBonus = 250_000m;
    private const decimal ExistingPresetReuseBonus = 300_000m;

    private static void AddCuratedOptions(
        IDictionary<string, CandidateOption> options,
        long allyCode,
        GacFormat format,
        IReadOnlyCollection<SquadDefinition> definitions,
        PlayerRosterSnapshot roster,
        IReadOnlyDictionary<string, PlayerRosterUnit> rosterById,
        IReadOnlySet<string> blocked)
    {
        int teamSize = (int)format;
        CuratedOption[] curated =
        [
            .. definitions
                .SelectMany(definition => definition.Variants.Select(variant => (Definition: definition, Variant: variant)))
                .Select(item => BuildCuratedOption(item.Definition, item.Variant, rosterById, blocked))
                .Where(option => option is not null)
                .Select(option => option!)
                .Where(option => option.Units.Count == teamSize)
                .OrderByDescending(option => option.Score)
                .ThenBy(option => option.Definition.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxCuratedCandidates)
        ];

        foreach (CuratedOption option in curated)
        {
            GacTeamPresetDetails preset = CreateCandidate(
                allyCode,
                format,
                $"Auto · {option.Definition.Name} · {option.Variant.Name}",
                MapUse(option.Definition.Use),
                option.Units[0],
                option.Units.Skip(1),
                isFleet: false,
                roster.UpdatedAtUtc);
            AddOption(options, new CandidateOption(
                preset,
                Generated: true,
                option.Score,
                UnitIds(preset)));
        }
    }

    private static CuratedOption? BuildCuratedOption(
        SquadDefinition definition,
        SquadVariant variant,
        IReadOnlyDictionary<string, PlayerRosterUnit> roster,
        IReadOnlySet<string> blocked)
    {
        var units = new List<PlayerRosterUnit>();
        foreach (string id in variant.AllUnitDefinitionIds)
        {
            if (blocked.Contains(id) ||
                !roster.TryGetValue(id, out PlayerRosterUnit? unit) ||
                unit.IsShip)
            {
                return null;
            }

            units.Add(unit);
        }

        decimal score = ScoreUnits(units, MapUse(definition.Use)) + CuratedBonus;
        return new CuratedOption(definition, variant, units, score);
    }

    private static void AddHeuristicCharacterOptions(
        IDictionary<string, CandidateOption> options,
        long allyCode,
        GacFormat format,
        PlayerRosterSnapshot roster,
        IReadOnlySet<string> blocked)
    {
        int teamSize = (int)format;
        if (teamSize <= 1)
        {
            return;
        }

        PlayerRosterUnit[] available =
        [
            .. roster.Units
                .Where(unit => !unit.IsShip && !blocked.Contains(unit.DefinitionId))
                .OrderByDescending(UnitStrength)
        ];
        foreach (PlayerRosterUnit leader in available.Take(MaxHeuristicLeaders))
        {
            AddHeuristicVariant(
                options,
                allyCode,
                format,
                roster.UpdatedAtUtc,
                leader,
                available
                    .Where(unit => unit.DefinitionId != leader.DefinitionId)
                    .OrderByDescending(unit => SharedFactionCount(leader, unit))
                    .ThenByDescending(UnitStrength)
                    .Take(teamSize - 1),
                "sinergia");
            AddHeuristicVariant(
                options,
                allyCode,
                format,
                roster.UpdatedAtUtc,
                leader,
                available
                    .Where(unit => unit.DefinitionId != leader.DefinitionId)
                    .OrderByDescending(UnitStrength)
                    .Take(teamSize - 1),
                "potencia");
        }
    }

    private static void AddHeuristicVariant(
        IDictionary<string, CandidateOption> options,
        long allyCode,
        GacFormat format,
        DateTimeOffset updatedAtUtc,
        PlayerRosterUnit leader,
        IEnumerable<PlayerRosterUnit> members,
        string variant)
    {
        PlayerRosterUnit[] memberArray = [.. members];
        if (memberArray.Length != (int)format - 1)
        {
            return;
        }

        PlayerRosterUnit[] units = [leader, .. memberArray];
        string faction = DominantFaction(units) ?? "mixto";
        GacTeamPresetDetails preset = CreateCandidate(
            allyCode,
            format,
            $"Auto · {faction} · {leader.Name} · {variant}",
            GacPlannerTeamUse.Defense,
            leader,
            memberArray,
            isFleet: false,
            updatedAtUtc);
        AddOption(options, new CandidateOption(
            preset,
            Generated: true,
            ScoreUnits(units, GacPlannerTeamUse.Defense),
            UnitIds(preset)));
    }

    private static void AddFleetOptions(
        IDictionary<string, CandidateOption> options,
        long allyCode,
        GacFormat format,
        PlayerRosterSnapshot roster,
        IReadOnlySet<string> blocked)
    {
        PlayerRosterUnit[] capitalShips =
        [
            .. roster.Units
                .Where(unit => unit.IsShip && IsCapitalShip(unit) && !blocked.Contains(unit.DefinitionId))
                .OrderByDescending(UnitStrength)
        ];
        PlayerRosterUnit[] combatShips =
        [
            .. roster.Units
                .Where(unit => unit.IsShip && !IsCapitalShip(unit) && !blocked.Contains(unit.DefinitionId))
                .OrderByDescending(UnitStrength)
        ];

        foreach (PlayerRosterUnit capital in capitalShips)
        {
            AddFleetVariant(
                options,
                allyCode,
                format,
                roster.UpdatedAtUtc,
                capital,
                combatShips
                    .OrderByDescending(ship => SharedFactionCount(capital, ship))
                    .ThenByDescending(UnitStrength)
                    .Take(MaxFleetMembers),
                "sinergia");
            AddFleetVariant(
                options,
                allyCode,
                format,
                roster.UpdatedAtUtc,
                capital,
                combatShips.Take(MaxFleetMembers),
                "potencia");
        }
    }

    private static void AddFleetVariant(
        IDictionary<string, CandidateOption> options,
        long allyCode,
        GacFormat format,
        DateTimeOffset updatedAtUtc,
        PlayerRosterUnit capital,
        IEnumerable<PlayerRosterUnit> members,
        string variant)
    {
        PlayerRosterUnit[] memberArray = [.. members];
        if (memberArray.Length == 0)
        {
            return;
        }

        PlayerRosterUnit[] units = [capital, .. memberArray];
        string faction = DominantFaction(units) ?? "flota";
        GacTeamPresetDetails preset = CreateCandidate(
            allyCode,
            format,
            $"Auto · {faction} · {capital.Name} · {variant}",
            GacPlannerTeamUse.Defense,
            capital,
            memberArray,
            isFleet: true,
            updatedAtUtc);
        AddOption(options, new CandidateOption(
            preset,
            Generated: true,
            ScoreUnits(units, GacPlannerTeamUse.Defense),
            UnitIds(preset)));
    }

    private static GacTeamPresetDetails CreateCandidate(
        long allyCode,
        GacFormat format,
        string name,
        GacPlannerTeamUse use,
        PlayerRosterUnit leader,
        IEnumerable<PlayerRosterUnit> members,
        bool isFleet,
        DateTimeOffset updatedAtUtc)
    {
        PlayerRosterUnit[] memberArray = [.. members];
        string identity = string.Join(
            '|',
            new[] { leader.DefinitionId }.Concat(memberArray.Select(unit => unit.DefinitionId)));
        Guid id = DeterministicGuid($"{allyCode}:{format}:{isFleet}:{identity}");
        return new GacTeamPresetDetails(
            id,
            allyCode,
            name,
            format,
            use,
            new GacPlannerSquadDetails(
                ToDetails(leader),
                [.. memberArray.Select(ToDetails)],
                isFleet),
            updatedAtUtc);
    }

    private static GacPlannerUnitDetails ToDetails(PlayerRosterUnit unit) => new(
        unit.DefinitionId,
        unit.Name,
        unit.ThumbnailName,
        unit.IsShip,
        unit.GalacticPower,
        unit.RelicTier,
        unit.ZetaCount,
        unit.OmicronCount,
        unit.Stats,
        unit.Mods);

    private static Guid DeterministicGuid(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static decimal ScorePreset(GacTeamPresetDetails preset) =>
        preset.Squad.AllUnits.Sum(unit => UnitStrength(unit)) +
        UseBonus(preset.Use) +
        ExistingPresetReuseBonus;

    private static decimal ScoreUnits(
        IReadOnlyCollection<PlayerRosterUnit> units,
        GacPlannerTeamUse use) =>
        units.Sum(UnitStrength) +
        UseBonus(use) +
        CohesionBonus(units);

    private static decimal UnitStrength(PlayerRosterUnit unit) =>
        unit.GalacticPower +
        (unit.RelicTier * 5_000m) +
        (unit.OmicronCount * 15_000m) +
        (unit.ZetaCount * 2_500m);

    private static decimal UnitStrength(GacPlannerUnitDetails unit) =>
        (unit.GalacticPower ?? 0L) +
        ((unit.RelicTier ?? 0) * 5_000m) +
        ((unit.OmicronCount ?? 0) * 15_000m) +
        ((unit.ZetaCount ?? 0) * 2_500m);

    private static decimal CohesionBonus(IReadOnlyCollection<PlayerRosterUnit> units)
    {
        PlayerRosterUnit[] array = [.. units];
        int sharedPairs = 0;
        for (int left = 0; left < array.Length; left++)
        {
            for (int right = left + 1; right < array.Length; right++)
            {
                sharedPairs += SharedFactionCount(array[left], array[right]);
            }
        }

        return Math.Min(200_000m, sharedPairs * 20_000m);
    }

    private static decimal UseBonus(GacPlannerTeamUse use) => use switch
    {
        GacPlannerTeamUse.Defense => 200_000m,
        GacPlannerTeamUse.Flexible => 50_000m,
        _ => -150_000m
    };

    private static int SharedFactionCount(PlayerRosterUnit left, PlayerRosterUnit right) =>
        left.Factions.Intersect(right.Factions, StringComparer.OrdinalIgnoreCase).Count();

    private static string? DominantFaction(IReadOnlyCollection<PlayerRosterUnit> units) =>
        units
            .SelectMany(unit => unit.Factions)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(item => item.Count >= 2)?.Name;

    private static bool IsCapitalShip(PlayerRosterUnit unit) =>
        unit.DefinitionId.StartsWith("CAPITAL", StringComparison.OrdinalIgnoreCase) ||
        unit.Name.Contains("capital", StringComparison.OrdinalIgnoreCase) ||
        unit.Tags.Any(tag => tag.Contains("capital", StringComparison.OrdinalIgnoreCase));

    private static GacPlannerTeamUse MapUse(SquadUse use) => use switch
    {
        SquadUse.Defense => GacPlannerTeamUse.Defense,
        SquadUse.Offense => GacPlannerTeamUse.Offense,
        _ => GacPlannerTeamUse.Flexible
    };

    private sealed record CuratedOption(
        SquadDefinition Definition,
        SquadVariant Variant,
        IReadOnlyList<PlayerRosterUnit> Units,
        decimal Score);
}
