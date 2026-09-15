using System.Security.Cryptography;
using System.Text;

using Swgoh.Application.Players;
using Swgoh.Application.Squads;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Squads;

namespace Swgoh.Application.Gac;

internal sealed record GacRosterDefenseCandidateSet(
    IReadOnlyCollection<GacTeamPresetDetails> Candidates,
    IReadOnlyDictionary<Guid, GacTeamPresetDetails> GeneratedById,
    IReadOnlyCollection<string> Warnings)
{
    public static GacRosterDefenseCandidateSet Empty { get; } = new(
        [],
        new Dictionary<Guid, GacTeamPresetDetails>(),
        []);
}

internal sealed record GacGeneratedPresetMaterialization(
    IReadOnlyDictionary<Guid, Guid> IdMap,
    IReadOnlyCollection<Guid> CreatedPresetIds);

internal sealed class GacRosterDefenseCandidateService(
    IPlayerRosterService rosterService,
    ISquadRepository squadRepository)
{
    private const int CandidateBuffer = 2;
    private const int MaxSquadDefinitions = 200;
    private const int MaxFleetMembers = 7;
    private static readonly TimeSpan MaterializationCleanupTimeout = TimeSpan.FromSeconds(10);

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

        PlayerRosterSnapshot? roster = await rosterService
            .GetSnapshotAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (roster is null || roster.Units.Count == 0)
        {
            return new GacRosterDefenseCandidateSet(
                [],
                new Dictionary<Guid, GacTeamPresetDetails>(),
                ["No hay snapshot del roster disponible para construir equipos defensivos automáticamente."]);
        }

        Dictionary<string, PlayerRosterUnit> rosterById = roster.Units.ToDictionary(
            unit => unit.DefinitionId,
            StringComparer.OrdinalIgnoreCase);
        var blocked = new HashSet<string>(blockedUnitIds ?? [], StringComparer.OrdinalIgnoreCase);
        AddReservedUnits(blocked, profile, existingPresets, battlePlan);

        HashSet<Guid> pinnedIds = profile.Slots
            .Where(slot => slot.PinnedTeamPresetId is not null)
            .Select(slot => slot.PinnedTeamPresetId!.Value)
            .ToHashSet();
        var occupied = new HashSet<string>(blocked, StringComparer.OrdinalIgnoreCase);
        foreach (GacTeamPresetDetails pinned in existingPresets.Where(preset => pinnedIds.Contains(preset.Id)))
        {
            AddUnits(occupied, pinned);
        }

        int requestedCharacters = profile.Slots.Count(slot =>
            slot.PinnedTeamPresetId is null && !IsFleetZone(slot.Zone));
        int requestedFleets = profile.Slots.Count(slot =>
            slot.PinnedTeamPresetId is null && IsFleetZone(slot.Zone));

        (int coveredCharacters, int coveredFleets) = ReserveExistingCoverage(
            existingPresets,
            profile,
            requestedCharacters,
            requestedFleets,
            occupied);
        int missingCharacters = Math.Max(0, requestedCharacters - coveredCharacters);
        int missingFleets = Math.Max(0, requestedFleets - coveredFleets);
        if (missingCharacters == 0 && missingFleets == 0)
        {
            return GacRosterDefenseCandidateSet.Empty;
        }

        var generated = new List<GacTeamPresetDetails>();
        var warnings = new List<string>
        {
            $"Faltan presets para cubrir la defensa; se construirán candidatos automáticamente desde tu roster ({missingCharacters} escuadra(s), {missingFleets} flota(s))."
        };

        int targetCharacters = missingCharacters + (missingCharacters > 0 ? CandidateBuffer : 0);
        if (targetCharacters > 0)
        {
            IReadOnlyCollection<SquadDefinition> definitions = await squadRepository
                .SearchAsync(
                    new SquadSearchQuery(Format: ToSquadFormat(format), Limit: MaxSquadDefinitions),
                    cancellationToken)
                .ConfigureAwait(false);
            AddCuratedCharacterTeams(
                allyCode,
                format,
                targetCharacters,
                definitions,
                roster,
                rosterById,
                occupied,
                generated);
            AddHeuristicCharacterTeams(
                allyCode,
                format,
                targetCharacters,
                roster,
                occupied,
                generated);
        }

        int targetFleets = missingFleets + (missingFleets > 0 ? CandidateBuffer : 0);
        if (targetFleets > 0)
        {
            int before = generated.Count;
            AddFleetTeams(
                allyCode,
                format,
                targetFleets,
                roster,
                occupied,
                generated);
            if (generated.Count == before)
            {
                warnings.Add("No se ha podido identificar una flota válida en el roster para cubrir los huecos de flota.");
            }
        }

        if (generated.Count > 0)
        {
            warnings.Add(
                $"Se han creado {generated.Count} candidato(s) temporales desde el roster. Solo los elegidos se guardarán como presets al aplicar la defensa.");
        }

        Dictionary<Guid, GacTeamPresetDetails> byId = generated.ToDictionary(item => item.Id);
        return new GacRosterDefenseCandidateSet(generated, byId, warnings);
    }

    public static async Task<GacGeneratedPresetMaterialization> MaterializeAsync(
        IGacPlannerService plannerService,
        long allyCode,
        GacFormat format,
        IReadOnlyCollection<GacSmartDefenseAssignment> assignments,
        GacRosterDefenseCandidateSet candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plannerService);
        var idMap = new Dictionary<Guid, Guid>();
        var created = new List<Guid>();

        try
        {
            foreach (Guid virtualId in assignments
                         .Select(item => item.TeamPresetId)
                         .Distinct()
                         .Where(candidates.GeneratedById.ContainsKey))
            {
                GacTeamPresetDetails candidate = candidates.GeneratedById[virtualId];
                GacTeamPresetDetails persisted = await plannerService.CreatePresetAsync(
                    allyCode,
                    new SaveGacTeamPreset(
                        candidate.Name,
                        format,
                        GacPlannerTeamUse.Defense,
                        candidate.Squad.Leader.DefinitionId,
                        [.. candidate.Squad.Members.Select(unit => unit.DefinitionId)],
                        candidate.Squad.IsFleet),
                    cancellationToken).ConfigureAwait(false);
                idMap[virtualId] = persisted.Id;
                created.Add(persisted.Id);
            }

            return new GacGeneratedPresetMaterialization(idMap, created);
        }
        catch
        {
            await RollbackMaterializationAsync(plannerService, allyCode, created).ConfigureAwait(false);
            throw;
        }
    }

    public static GacSmartDefenseAssignment Remap(
        GacSmartDefenseAssignment assignment,
        IReadOnlyDictionary<Guid, Guid> idMap) =>
        idMap.TryGetValue(assignment.TeamPresetId, out Guid persistedId)
            ? assignment with { TeamPresetId = persistedId }
            : assignment;

    public static async Task RollbackMaterializationAsync(
        IGacPlannerService plannerService,
        long allyCode,
        IReadOnlyCollection<Guid> createdPresetIds)
    {
        ArgumentNullException.ThrowIfNull(plannerService);
        if (createdPresetIds.Count == 0)
        {
            return;
        }

        using var cleanupSource = new CancellationTokenSource(MaterializationCleanupTimeout);
        foreach (Guid id in createdPresetIds.Reverse())
        {
            try
            {
                await plannerService
                    .DeletePresetAsync(allyCode, id, cleanupSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cleanupSource.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Best effort compensation must not hide the original materialization/save failure.
            }
        }
    }

    private static void AddReservedUnits(
        HashSet<string> blocked,
        GacDefenseStrategyProfile profile,
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        CurrentGacBattlePlan? battlePlan)
    {
        HashSet<Guid> reservedPresetIds = profile.ReservedAttackPresetIds.ToHashSet();
        foreach (GacTeamPresetDetails preset in presets.Where(item => reservedPresetIds.Contains(item.Id)))
        {
            AddUnits(blocked, preset);
        }

        foreach (GacBattleAttackReserve reserve in battlePlan?.AttackReserves ?? [])
        {
            blocked.Add(reserve.Unit.DefinitionId);
        }
    }

    private static (int Characters, int Fleets) ReserveExistingCoverage(
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        GacDefenseStrategyProfile profile,
        int requestedCharacters,
        int requestedFleets,
        HashSet<string> occupied)
    {
        HashSet<Guid> unavailableIds = profile.ReservedAttackPresetIds
            .Concat(profile.Slots
                .Where(slot => slot.PinnedTeamPresetId is not null)
                .Select(slot => slot.PinnedTeamPresetId!.Value))
            .ToHashSet();
        int characters = 0;
        int fleets = 0;

        foreach (GacTeamPresetDetails preset in presets
                     .Where(item => !unavailableIds.Contains(item.Id))
                     .OrderBy(PresetUsePriority)
                     .ThenByDescending(TeamPower))
        {
            if (preset.Squad.AllUnits.Any(unit => occupied.Contains(unit.DefinitionId)))
            {
                continue;
            }

            if (preset.Squad.IsFleet)
            {
                if (fleets >= requestedFleets)
                {
                    continue;
                }

                fleets++;
            }
            else
            {
                if (characters >= requestedCharacters)
                {
                    continue;
                }

                characters++;
            }

            AddUnits(occupied, preset);
        }

        return (characters, fleets);
    }

    private static void AddCuratedCharacterTeams(
        long allyCode,
        GacFormat format,
        int targetCount,
        IReadOnlyCollection<SquadDefinition> definitions,
        PlayerRosterSnapshot roster,
        IReadOnlyDictionary<string, PlayerRosterUnit> rosterById,
        HashSet<string> occupied,
        List<GacTeamPresetDetails> generated)
    {
        IEnumerable<CuratedCandidate> candidates = definitions
            .SelectMany(definition => definition.Variants.Select(variant => new { definition, variant }))
            .Select(item => BuildCuratedCandidate(item.definition, item.variant, rosterById))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Definition.Name, StringComparer.OrdinalIgnoreCase);

        foreach (CuratedCandidate item in candidates)
        {
            if (generated.Count(candidate => !candidate.Squad.IsFleet) >= targetCount)
            {
                break;
            }
            if (item.Units.Any(unit => occupied.Contains(unit.DefinitionId)))
            {
                continue;
            }

            GacTeamPresetDetails preset = CreateCandidate(
                allyCode,
                format,
                $"Auto · {item.Definition.Name} · {item.Variant.Name}",
                MapUse(item.Definition.Use),
                item.Units[0],
                item.Units.Skip(1),
                isFleet: false,
                roster.UpdatedAtUtc);
            generated.Add(preset);
            AddUnits(occupied, preset);
        }
    }

    private static CuratedCandidate? BuildCuratedCandidate(
        SquadDefinition definition,
        SquadVariant variant,
        IReadOnlyDictionary<string, PlayerRosterUnit> roster)
    {
        var units = new List<PlayerRosterUnit>();
        foreach (string id in variant.AllUnitDefinitionIds)
        {
            if (!roster.TryGetValue(id, out PlayerRosterUnit? unit) || unit.IsShip)
            {
                return null;
            }
            units.Add(unit);
        }

        decimal score = units.Sum(UnitStrength) + UseBonus(definition.Use);
        return new CuratedCandidate(definition, variant, units, score);
    }

    private static void AddHeuristicCharacterTeams(
        long allyCode,
        GacFormat format,
        int targetCount,
        PlayerRosterSnapshot roster,
        HashSet<string> occupied,
        List<GacTeamPresetDetails> generated)
    {
        int teamSize = (int)format;
        var remaining = roster.Units
            .Where(unit => !unit.IsShip && !occupied.Contains(unit.DefinitionId))
            .OrderByDescending(UnitStrength)
            .ToList();

        while (generated.Count(candidate => !candidate.Squad.IsFleet) < targetCount && remaining.Count >= teamSize)
        {
            PlayerRosterUnit anchor = remaining[0];
            remaining.RemoveAt(0);
            var team = new List<PlayerRosterUnit> { anchor };
            while (team.Count < teamSize && remaining.Count > 0)
            {
                PlayerRosterUnit next = remaining
                    .OrderByDescending(candidate => Affinity(candidate, team))
                    .ThenByDescending(UnitStrength)
                    .First();
                remaining.Remove(next);
                team.Add(next);
            }

            if (team.Count != teamSize)
            {
                break;
            }

            string faction = DominantFaction(team) ?? "mixto";
            GacTeamPresetDetails preset = CreateCandidate(
                allyCode,
                format,
                $"Auto · {faction} · {anchor.Name}",
                GacPlannerTeamUse.Defense,
                anchor,
                team.Skip(1),
                isFleet: false,
                roster.UpdatedAtUtc);
            generated.Add(preset);
            AddUnits(occupied, preset);
        }
    }

    private static void AddFleetTeams(
        long allyCode,
        GacFormat format,
        int targetCount,
        PlayerRosterSnapshot roster,
        HashSet<string> occupied,
        List<GacTeamPresetDetails> generated)
    {
        List<PlayerRosterUnit> capitalShips = roster.Units
            .Where(unit => unit.IsShip && IsCapitalShip(unit) && !occupied.Contains(unit.DefinitionId))
            .OrderByDescending(UnitStrength)
            .ToList();
        var combatShips = roster.Units
            .Where(unit => unit.IsShip && !IsCapitalShip(unit) && !occupied.Contains(unit.DefinitionId))
            .OrderByDescending(UnitStrength)
            .ToList();

        foreach (PlayerRosterUnit capital in capitalShips)
        {
            if (generated.Count(candidate => candidate.Squad.IsFleet) >= targetCount || combatShips.Count == 0)
            {
                break;
            }

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

            foreach (PlayerRosterUnit member in members)
            {
                combatShips.Remove(member);
            }

            string faction = DominantFaction([capital, .. members]) ?? "flota";
            GacTeamPresetDetails preset = CreateCandidate(
                allyCode,
                format,
                $"Auto · {faction} · {capital.Name}",
                GacPlannerTeamUse.Defense,
                capital,
                members,
                isFleet: true,
                roster.UpdatedAtUtc);
            generated.Add(preset);
            AddUnits(occupied, preset);
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

    private static decimal UnitStrength(PlayerRosterUnit unit) =>
        unit.GalacticPower +
        (unit.RelicTier * 5_000m) +
        (unit.OmicronCount * 15_000m) +
        (unit.ZetaCount * 2_500m);

    private static decimal Affinity(PlayerRosterUnit candidate, IReadOnlyCollection<PlayerRosterUnit> team) =>
        team.Sum(member => SharedFactionCount(candidate, member)) * 1_000_000m + UnitStrength(candidate);

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

    private static int PresetUsePriority(GacTeamPresetDetails preset) => preset.Use switch
    {
        GacPlannerTeamUse.Defense => 0,
        GacPlannerTeamUse.Flexible => 1,
        _ => 2
    };

    private static decimal UseBonus(SquadUse use) => use switch
    {
        SquadUse.Defense => 40_000m,
        SquadUse.Flexible => 20_000m,
        _ => 0m
    };

    private static GacPlannerTeamUse MapUse(SquadUse use) => use switch
    {
        SquadUse.Defense => GacPlannerTeamUse.Defense,
        SquadUse.Offense => GacPlannerTeamUse.Offense,
        _ => GacPlannerTeamUse.Flexible
    };

    private static SquadFormat ToSquadFormat(GacFormat format) => format == GacFormat.ThreeVsThree
        ? SquadFormat.ThreeVsThree
        : SquadFormat.FiveVsFive;

    private static long TeamPower(GacTeamPresetDetails preset) =>
        preset.Squad.AllUnits.Sum(unit => unit.GalacticPower ?? 0L);

    private static void AddUnits(HashSet<string> target, GacTeamPresetDetails preset)
    {
        foreach (GacPlannerUnitDetails unit in preset.Squad.AllUnits)
        {
            target.Add(unit.DefinitionId);
        }
    }

    private sealed record CuratedCandidate(
        SquadDefinition Definition,
        SquadVariant Variant,
        IReadOnlyList<PlayerRosterUnit> Units,
        decimal Score);
}
