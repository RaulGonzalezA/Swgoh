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

internal sealed partial class OptimizedGacRosterDefenseCandidateProvider(
    IPlayerRosterService rosterService,
    ISquadRepository squadRepository) : IGacRosterDefenseCandidateProvider
{
    private const int MaxSquadDefinitions = 200;
    private const int MaxOptionsPerType = 96;
    private const int BeamWidth = 256;

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
        $"{preset.Squad.IsFleet}:{preset.Squad.Leader.DefinitionId}:{string.Join('|', UnitIds(preset).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}";

    private static string SelectionKey(IReadOnlyCollection<CandidateOption> selected) =>
        string.Join('|', selected.Select(option => option.Preset.Id.ToString("N")).OrderBy(id => id, StringComparer.Ordinal));

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

    private static bool IsFleetZone(string zone) =>
        zone.Contains("flota", StringComparison.OrdinalIgnoreCase) ||
        zone.Contains("fleet", StringComparison.OrdinalIgnoreCase);

    private static SquadFormat ToSquadFormat(GacFormat format) => format == GacFormat.ThreeVsThree
        ? SquadFormat.ThreeVsThree
        : SquadFormat.FiveVsFive;

    private sealed record CandidateOption(
        GacTeamPresetDetails Preset,
        bool Generated,
        decimal Score,
        IReadOnlyCollection<string> UnitIds);

    private sealed record SearchState(
        IReadOnlyList<CandidateOption> Selected,
        HashSet<string> UsedUnits,
        decimal Score);
}
