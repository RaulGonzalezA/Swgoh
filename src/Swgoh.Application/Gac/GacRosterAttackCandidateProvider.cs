using System.Security.Cryptography;
using System.Text;

using Swgoh.Application.Players;
using Swgoh.Application.Squads;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Squads;

namespace Swgoh.Application.Gac;

internal interface IGacRosterAttackCandidateProvider
{
    Task<GacRosterAttackCandidateSet> BuildAsync(
        long allyCode,
        GacFormat format,
        IReadOnlyCollection<GacTeamPresetDetails> existingPresets,
        IReadOnlyCollection<string> blockedUnitIds,
        CancellationToken cancellationToken = default);
}

internal sealed record GacRosterAttackCandidateSet(
    IReadOnlyCollection<GacTeamPresetDetails> Candidates,
    IReadOnlyDictionary<Guid, GacTeamPresetDetails> GeneratedById)
{
    public static GacRosterAttackCandidateSet Empty { get; } = new(
        [],
        new Dictionary<Guid, GacTeamPresetDetails>());
}

internal sealed class GacRosterAttackCandidateProvider(
    IPlayerRosterService rosterService,
    ISquadRepository squadRepository) : IGacRosterAttackCandidateProvider
{
    private const int MaxSquadDefinitions = 200;
    private const int MaxCuratedCandidates = 64;
    private const int MaxHeuristicLeaders = 24;
    private const int MaxFleetMembers = 7;
    private const int MaxGeneratedCandidates = 128;

    public async Task<GacRosterAttackCandidateSet> BuildAsync(
        long allyCode,
        GacFormat format,
        IReadOnlyCollection<GacTeamPresetDetails> existingPresets,
        IReadOnlyCollection<string> blockedUnitIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(existingPresets);
        ArgumentNullException.ThrowIfNull(blockedUnitIds);

        PlayerRosterSnapshot? roster = await rosterService
            .GetSnapshotAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (roster is null || roster.Units.Count == 0)
        {
            return GacRosterAttackCandidateSet.Empty;
        }

        var blocked = new HashSet<string>(blockedUnitIds, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, PlayerRosterUnit> rosterById = roster.Units.ToDictionary(
            unit => unit.DefinitionId,
            StringComparer.OrdinalIgnoreCase);
        var options = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> existingKeys = existingPresets
            .Select(PresetKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyCollection<SquadDefinition> definitions = await squadRepository
            .SearchAsync(
                new SquadSearchQuery(Format: ToSquadFormat(format), Limit: MaxSquadDefinitions),
                cancellationToken)
            .ConfigureAwait(false);
        AddCuratedCandidates(
            options,
            existingKeys,
            allyCode,
            format,
            definitions,
            roster,
            rosterById,
            blocked);
        AddHeuristicCharacterCandidates(
            options,
            existingKeys,
            allyCode,
            format,
            roster,
            blocked);
        AddFleetCandidates(
            options,
            existingKeys,
            allyCode,
            format,
            roster,
            blocked);

        GacTeamPresetDetails[] generated =
        [
            .. options.Values
                .OrderByDescending(option => option.Score)
                .ThenBy(option => option.Preset.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxGeneratedCandidates)
                .Select(option => option.Preset)
        ];
        return new GacRosterAttackCandidateSet(
            generated,
            generated.ToDictionary(item => item.Id));
    }

    private static void AddCuratedCandidates(
        IDictionary<string, Candidate> options,
        IReadOnlySet<string> existingKeys,
        long allyCode,
        GacFormat format,
        IReadOnlyCollection<SquadDefinition> definitions,
        PlayerRosterSnapshot roster,
        IReadOnlyDictionary<string, PlayerRosterUnit> rosterById,
        IReadOnlySet<string> blocked)
    {
        int teamSize = (int)format;
        foreach ((SquadDefinition definition, SquadVariant variant) in definitions
                     .Where(definition => definition.Use != SquadUse.Defense)
                     .SelectMany(definition => definition.Variants.Select(variant => (definition, variant)))
                     .Take(MaxCuratedCandidates))
        {
            PlayerRosterUnit[] units =
            [
                .. variant.AllUnitDefinitionIds
                    .Select(id => rosterById.GetValueOrDefault(id))
                    .Where(unit => unit is not null)
                    .Select(unit => unit!)
            ];
            if (units.Length != teamSize ||
                units.Any(unit => unit.IsShip || blocked.Contains(unit.DefinitionId)) ||
                variant.AllUnitDefinitionIds.Count != units.Length)
            {
                continue;
            }

            GacTeamPresetDetails preset = CreateCandidate(
                allyCode,
                format,
                $"Auto ATK · {definition.Name} · {variant.Name}",
                definition.Use == SquadUse.Offense ? GacPlannerTeamUse.Offense : GacPlannerTeamUse.Flexible,
                units[0],
                units.Skip(1),
                isFleet: false,
                roster.UpdatedAtUtc);
            AddOption(options, existingKeys, preset, ScoreUnits(units) + 250_000m);
        }
    }

    private static void AddHeuristicCharacterCandidates(
        IDictionary<string, Candidate> options,
        IReadOnlySet<string> existingKeys,
        long allyCode,
        GacFormat format,
        PlayerRosterSnapshot roster,
        IReadOnlySet<string> blocked)
    {
        int teamSize = (int)format;
        PlayerRosterUnit[] available =
        [
            .. roster.Units
                .Where(unit => !unit.IsShip && !blocked.Contains(unit.DefinitionId))
                .OrderByDescending(UnitStrength)
        ];
        foreach (PlayerRosterUnit leader in available.Take(MaxHeuristicLeaders))
        {
            AddCharacterVariant(
                options,
                existingKeys,
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
            AddCharacterVariant(
                options,
                existingKeys,
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

    private static void AddCharacterVariant(
        IDictionary<string, Candidate> options,
        IReadOnlySet<string> existingKeys,
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
            $"Auto ATK · {faction} · {leader.Name} · {variant}",
            GacPlannerTeamUse.Offense,
            leader,
            memberArray,
            isFleet: false,
            updatedAtUtc);
        AddOption(options, existingKeys, preset, ScoreUnits(units));
    }

    private static void AddFleetCandidates(
        IDictionary<string, Candidate> options,
        IReadOnlySet<string> existingKeys,
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
            PlayerRosterUnit[] members =
            [
                .. combatShips
                    .OrderByDescending(ship => SharedFactionCount(capital, ship))
                    .ThenByDescending(UnitStrength)
                    .Take(MaxFleetMembers)
            ];
            if (members.Length == 0)
            {
                continue;
            }

            PlayerRosterUnit[] units = [capital, .. members];
            string faction = DominantFaction(units) ?? "flota";
            GacTeamPresetDetails preset = CreateCandidate(
                allyCode,
                format,
                $"Auto ATK · {faction} · {capital.Name}",
                GacPlannerTeamUse.Offense,
                capital,
                members,
                isFleet: true,
                roster.UpdatedAtUtc);
            AddOption(options, existingKeys, preset, ScoreUnits(units));
        }
    }

    private static void AddOption(
        IDictionary<string, Candidate> options,
        IReadOnlySet<string> existingKeys,
        GacTeamPresetDetails preset,
        decimal score)
    {
        string key = PresetKey(preset);
        if (existingKeys.Contains(key))
        {
            return;
        }

        if (!options.TryGetValue(key, out Candidate? existing) || score > existing.Score)
        {
            options[key] = new Candidate(preset, score);
        }
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
        string identity = string.Join('|', new[] { leader.DefinitionId }.Concat(memberArray.Select(unit => unit.DefinitionId)));
        Guid id = DeterministicGuid($"counter:{allyCode}:{format}:{isFleet}:{identity}");
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

    private static string PresetKey(GacTeamPresetDetails preset) =>
        $"{preset.Squad.IsFleet}:{string.Join('|', preset.Squad.AllUnits.Select(unit => unit.DefinitionId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}";

    private static decimal ScoreUnits(IReadOnlyCollection<PlayerRosterUnit> units) =>
        units.Sum(UnitStrength) + Math.Min(200_000m, CohesionPairs(units) * 20_000m);

    private static decimal UnitStrength(PlayerRosterUnit unit) =>
        unit.GalacticPower +
        (unit.RelicTier * 5_000m) +
        (unit.OmicronCount * 15_000m) +
        (unit.ZetaCount * 2_500m);

    private static int CohesionPairs(IReadOnlyCollection<PlayerRosterUnit> units)
    {
        PlayerRosterUnit[] array = [.. units];
        int shared = 0;
        for (int left = 0; left < array.Length; left++)
        {
            for (int right = left + 1; right < array.Length; right++)
            {
                shared += SharedFactionCount(array[left], array[right]);
            }
        }

        return shared;
    }

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

    private static SquadFormat ToSquadFormat(GacFormat format) => format == GacFormat.ThreeVsThree
        ? SquadFormat.ThreeVsThree
        : SquadFormat.FiveVsFive;

    private sealed record Candidate(GacTeamPresetDetails Preset, decimal Score);
}
