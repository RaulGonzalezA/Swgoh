using System.Security.Cryptography;
using System.Text;

using Swgoh.Application.Players;
using Swgoh.Application.Squads;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Squads;

namespace Swgoh.Application.Gac;

internal interface IGacRosterDefenseCandidateProvider
{
    Task<GacRosterDefenseCandidateSet> BuildAsync(
        long allyCode,
        GacFormat format,
        GacDefenseStrategyProfile profile,
        IReadOnlyCollection<GacTeamPresetDetails> existingPresets,
        CurrentGacBattlePlan? battlePlan,
        IReadOnlyCollection<string>? blockedUnitIds,
        CancellationToken cancellationToken = default);
}

internal sealed class OptimizedGacRosterDefenseCandidateProvider(
    IPlayerRosterService rosterService,
    ISquadRepository squadRepository) : IGacRosterDefenseCandidateProvider
{
    private const int MaxSquadDefinitions = 200;
    private const int MaxCuratedCandidates = 64;
    private const int MaxHeuristicLeaders = 24;
    private const int MaxOptionsPerType = 96;
    private const int MaxFleetMembers = 7;
    private const int BeamWidth = 256;
    private const decimal CuratedBonus = 250_000m;

    public async Task<GacRosterDefenseCandidateSet> BuildAsync(
        long allyCode,
        GacFormat format,
        GacDefenseStrategyProfile profile,
        IReadOnlyCollection<GacTeamPresetDetails> existingPresets,
        CurrentGacBattlePlan? battlePlan,
        IReadOnlyCollection<string>? blockedUnitIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(existingPresets);

        int requestedCharacters = profile.Slots.Count(slot =>
            slot.PinnedTeamPresetId is null && !IsFleetZone(slot.Zone));
        int requestedFleets = profile.Slots.Count(slot =>
            slot.PinnedTeamPresetId is null && IsFleetZone(slot.Zone));
        if (requestedCharacters == 0 && requestedFleets == 0)
        {
            return GacRosterDefenseCandidateSet.Empty;
        }

        PlayerRosterSnapshot? roster = await rosterService
            .GetSnapshotAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (roster is null || roster.Units.Count == 0)
        {
            return new GacRosterDefenseCandidateSet(
                [],
                new Dictionary<Guid, GacTeamPresetDetails>(),
                ["No hay snapshot del roster disponible para optimizar la defensa automáticamente."]);
        }

        var blocked = new HashSet<string>(blockedUnitIds ?? [], StringComparer.OrdinalIgnoreCase);
        HashSet<Guid> pinnedIds = profile.Slots
            .Where(slot => slot.PinnedTeamPresetId is not null)
            .Select(slot => slot.PinnedTeamPresetId!.Value)
            .ToHashSet();
        HashSet<Guid> reservedIds = profile.ReservedAttackPresetIds.ToHashSet();
        AddFixedPresetUnits(blocked, existingPresets, pinnedIds, reservedIds);
        foreach (GacBattleAttackReserve reserve in battlePlan?.AttackReserves ?? [])
        {
            blocked.Add(reserve.Unit.DefinitionId);
        }

        Dictionary<string, PlayerRosterUnit> rosterById = roster.Units.ToDictionary(
            unit => unit.DefinitionId,
            StringComparer.OrdinalIgnoreCase);
        var options = new Dictionary<string, CandidateOption>(StringComparer.OrdinalIgnoreCase);

        AddExistingOptions(options, existingPresets, pinnedIds, reservedIds, blocked);

        IReadOnlyCollection<SquadDefinition> definitions = requestedCharacters > 0
            ? await squadRepository.SearchAsync(
                    new SquadSearchQuery(Format: ToSquadFormat(format), Limit: MaxSquadDefinitions),
                    cancellationToken)
                .ConfigureAwait(false)
            : [];

        AddCuratedOptions(
            options,
            allyCode,
            format,
            definitions,
            roster,
            rosterById,
            blocked);
        AddHeuristicCharacterOptions(options, allyCode, format, roster, blocked);
        AddFleetOptions(options, allyCode, format, roster, blocked);

        CandidateOption[] characterSelection = Optimize(
            options.Values,
            isFleet: false,
            requestedCharacters);
        CandidateOption[] fleetSelection = Optimize(
            options.Values,
            isFleet: true,
            requestedFleets);
        CandidateOption[] selected = [.. characterSelection, .. fleetSelection];
        GacTeamPresetDetails[] generated =
        [
            .. selected
                .Where(option => option.Generated)
                .Select(option => option.Preset)
                .DistinctBy(preset => preset.Id)
        ];

        var warnings = new List<string>();
        AddCoverageWarning(warnings, "escuadras", requestedCharacters, characterSelection.Length);
        AddCoverageWarning(warnings, "flotas", requestedFleets, fleetSelection.Length);
        if (generated.Length > 0)
        {
            warnings.Add(
                $"La composición defensiva del roster se optimizó de forma conjunta: {generated.Length} equipo(s) temporal(es) complementan los presets existentes sin repetir unidades.");
        }
        if (options.Count > 0)
        {
            warnings.Add(
                $"Se evaluaron {options.Count} combinaciones candidatas con búsqueda acotada, priorizando cobertura completa y después valor defensivo total.");
        }

        Dictionary<Guid, GacTeamPresetDetails> byId = generated.ToDictionary(item => item.Id);
        return new GacRosterDefenseCandidateSet(generated, byId, warnings);
    }

    private static void AddExistingOptions(
        IDictionary<string, CandidateOption> options,
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        IReadOnlySet<Guid> pinnedIds,
        IReadOnlySet<Guid> reservedIds,
        IReadOnlySet<string> blocked)
    {
        foreach (GacTeamPresetDetails preset in presets)
        {
            if (pinnedIds.Contains(preset.Id) || reservedIds.Contains(preset.Id))
            {
                continue;
            }
            string[] unitIds = UnitIds(preset);
            if (unitIds.Any(blocked.Contains))
            {
                continue;
            }

            AddOption(options, new CandidateOption(
                preset,
                Generated: false,
                ScorePreset(preset),
                unitIds));
        }
    }

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

    private static CandidateOption[] Optimize(
        IEnumerable<CandidateOption> source,
        bool isFleet,
        int targetCount)
    {
        if (targetCount <= 0)
        {
            return [];
        }

        CandidateOption[] candidates =
        [
            .. source
                .Where(option => option.Preset.Squad.IsFleet == isFleet)
                .OrderByDescending(option => option.Score)
                .ThenBy(option => option.Preset.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxOptionsPerType)
        ];
        var states = new List<SearchState>
        {
            new([], new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0m)
        };

        foreach (CandidateOption candidate in candidates)
        {
            var expanded = new List<SearchState>(states.Count * 2);
            expanded.AddRange(states);
            foreach (SearchState state in states)
            {
                if (state.Selected.Count >= targetCount || candidate.UnitIds.Any(state.UsedUnits.Contains))
                {
                    continue;
                }

                var used = new HashSet<string>(state.UsedUnits, StringComparer.OrdinalIgnoreCase);
                used.UnionWith(candidate.UnitIds);
                expanded.Add(new SearchState(
                    [.. state.Selected, candidate],
                    used,
                    state.Score + candidate.Score));
            }

            states =
            [
                .. expanded
                    .OrderByDescending(state => state.Selected.Count)
                    .ThenByDescending(state => state.Score)
                    .ThenBy(state => SelectionKey(state.Selected), StringComparer.Ordinal)
                    .Take(BeamWidth)
            ];
        }

        SearchState best = states
            .OrderByDescending(state => state.Selected.Count)
            .ThenByDescending(state => state.Score)
            .ThenBy(state => SelectionKey(state.Selected), StringComparer.Ordinal)
            .First();
        return [.. best.Selected];
    }

    private static void AddOption(
        IDictionary<string, CandidateOption> options,
        CandidateOption option)
    {
        string key = OptionKey(option.Preset);
        if (!options.TryGetValue(key, out CandidateOption? current) || option.Score > current.Score)
        {
            options[key] = option;
        }
    }

    private static string OptionKey(GacTeamPresetDetails preset) =>
        $"{preset.Squad.IsFleet}:{preset.Squad.Leader.DefinitionId}:{string.Join('|', UnitIds(preset).Order(StringComparer.OrdinalIgnoreCase))}";

    private static string SelectionKey(IReadOnlyCollection<CandidateOption> selected) =>
        string.Join('|', selected.Select(option => option.Preset.Id.ToString("N")).Order(StringComparer.Ordinal));

    private static string[] UnitIds(GacTeamPresetDetails preset) =>
        [.. preset.Squad.AllUnits.Select(unit => unit.DefinitionId)];

    private static void AddFixedPresetUnits(
        HashSet<string> blocked,
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        IReadOnlySet<Guid> pinnedIds,
        IReadOnlySet<Guid> reservedIds)
    {
        foreach (GacTeamPresetDetails preset in presets.Where(preset =>
                     pinnedIds.Contains(preset.Id) || reservedIds.Contains(preset.Id)))
        {
            blocked.UnionWith(UnitIds(preset));
        }
    }

    private static void AddCoverageWarning(
        ICollection<string> warnings,
        string label,
        int requested,
        int selected)
    {
        if (selected < requested)
        {
            warnings.Add($"La optimización conjunta solo puede cubrir {selected}/{requested} {label} con las unidades disponibles y las reservas actuales.");
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
        preset.Squad.AllUnits.Sum(UnitStrength) + UseBonus(preset.Use);

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

    private static bool IsFleetZone(string zone) =>
        zone.Contains("flota", StringComparison.OrdinalIgnoreCase) ||
        zone.Contains("fleet", StringComparison.OrdinalIgnoreCase);

    private static GacPlannerTeamUse MapUse(SquadUse use) => use switch
    {
        SquadUse.Defense => GacPlannerTeamUse.Defense,
        SquadUse.Offense => GacPlannerTeamUse.Offense,
        _ => GacPlannerTeamUse.Flexible
    };

    private static SquadFormat ToSquadFormat(GacFormat format) => format == GacFormat.ThreeVsThree
        ? SquadFormat.ThreeVsThree
        : SquadFormat.FiveVsFive;

    private sealed record CandidateOption(
        GacTeamPresetDetails Preset,
        bool Generated,
        decimal Score,
        IReadOnlyCollection<string> UnitIds);

    private sealed record CuratedOption(
        SquadDefinition Definition,
        SquadVariant Variant,
        IReadOnlyList<PlayerRosterUnit> Units,
        decimal Score);

    private sealed record SearchState(
        IReadOnlyList<CandidateOption> Selected,
        HashSet<string> UsedUnits,
        decimal Score);
}
