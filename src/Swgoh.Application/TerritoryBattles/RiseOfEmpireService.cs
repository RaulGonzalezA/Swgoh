using System.Globalization;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

public interface IRiseOfEmpireService
{
    Task<RiseOfEmpireAnalysis?> GetAsync(long allyCode, CancellationToken cancellationToken = default);
}

internal sealed class RiseOfEmpireService(
    IPlayerProfileService playerProfileService,
    ISwgohGameDataCatalog gameDataCatalog) : IRiseOfEmpireService
{
    private const int TeamSize = 5;

    public async Task<RiseOfEmpireAnalysis?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(allyCode, cancellationToken);
        Task<GameDataCatalog> catalogTask = gameDataCatalog.GetAsync(cancellationToken);
        await Task.WhenAll(playerTask, catalogTask).ConfigureAwait(false);

        PlayerProfile? player = await playerTask.ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        GameDataCatalog catalog = await catalogTask.ConfigureAwait(false);
        RosterCandidate[] roster =
        [
            .. player.Roster
                .Where(unit => !unit.IsShip)
                .Select(unit => ToCandidate(unit, catalog))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
        ];

        RiseOfEmpirePlanetAnalysis[] planets =
        [
            .. RiseOfEmpireCatalog.Planets.Select(planet => AnalyzePlanet(planet, roster))
        ];
        RiseOfEmpirePhaseAnalysis[] phases =
        [
            .. planets
                .GroupBy(planet => RiseOfEmpireCatalog.Planets.First(definition => definition.Id == planet.Id).Phase)
                .OrderBy(group => group.Key)
                .Select(group => new RiseOfEmpirePhaseAnalysis(
                    group.Key,
                    group.Min(planet => planet.MinimumRelicTier),
                    [.. group]))
        ];

        IReadOnlyCollection<RiseOfEmpireUpgradePriority> priorities = BuildUpgradePriorities(planets);
        return new RiseOfEmpireAnalysis(
            player.AllyCode,
            player.Name,
            player.UpdatedAtUtc,
            RiseOfEmpireCatalog.Version,
            phases,
            priorities);
    }

    private static RiseOfEmpirePlanetAnalysis AnalyzePlanet(
        RiseOfEmpirePlanetDefinition planet,
        IReadOnlyCollection<RosterCandidate> roster)
    {
        RiseOfEmpireTeamRecommendation[] recommendations =
        [
            .. planet.Archetypes
                .Select(archetype => AnalyzeArchetype(planet, archetype, roster))
                .Where(recommendation => recommendation.Team.Count > 0)
                .OrderByDescending(recommendation => recommendation.Ready)
                .ThenByDescending(recommendation => recommendation.Score)
        ];
        RiseOfEmpireMissionReadiness[] missions =
        [
            .. planet.Missions.Select(mission => AnalyzeMission(mission, roster))
        ];

        decimal teamReadiness = recommendations.Length == 0
            ? 0m
            : recommendations.Max(recommendation => recommendation.ReadyUnits / (decimal)TeamSize * 100m);
        if (planet.IsBonusZone && missions.Any(mission => !mission.Ready))
        {
            teamReadiness = Math.Min(teamReadiness, 80m);
        }

        int eligibleCharacters = roster.Count(candidate => IsMissionReady(candidate.Unit, planet.MinimumRelicTier));
        return new RiseOfEmpirePlanetAnalysis(
            planet.Id,
            planet.Name,
            planet.Alignment,
            planet.MinimumRelicTier,
            planet.IsBonusZone,
            planet.StarThresholds,
            eligibleCharacters,
            recommendations.Count(recommendation => recommendation.Ready),
            Math.Round(Math.Clamp(teamReadiness, 0m, 100m), 1),
            recommendations,
            missions);
    }

    private static RiseOfEmpireTeamRecommendation AnalyzeArchetype(
        RiseOfEmpirePlanetDefinition planet,
        RiseOfEmpireArchetypeDefinition archetype,
        IReadOnlyCollection<RosterCandidate> roster)
    {
        RosterCandidate[] matching =
        [
            .. roster
                .Where(candidate => MatchesTag(candidate.Definition, archetype.TagKeyword))
                .OrderByDescending(candidate => IsMissionReady(candidate.Unit, planet.MinimumRelicTier))
                .ThenByDescending(candidate => candidate.Unit.RelicTier)
                .ThenByDescending(candidate => candidate.Unit.GalacticPower)
                .ThenByDescending(candidate => candidate.Unit.Stats?.Speed ?? 0m)
        ];

        RosterCandidate[] team = [.. matching.Take(TeamSize)];
        int readyUnits = team.Count(candidate => IsMissionReady(candidate.Unit, planet.MinimumRelicTier));
        bool ready = readyUnits >= TeamSize;
        RiseOfEmpireUnit[] view =
        [
            .. team.Select(candidate => ToView(candidate, planet.MinimumRelicTier))
        ];
        RiseOfEmpireUnit[] nextUpgrades =
        [
            .. matching
                .Where(candidate => !IsMissionReady(candidate.Unit, planet.MinimumRelicTier))
                .OrderBy(candidate => RelicsMissing(candidate.Unit, planet.MinimumRelicTier))
                .ThenByDescending(candidate => candidate.Unit.GalacticPower)
                .Take(Math.Max(0, TeamSize - readyUnits))
                .Select(candidate => ToView(candidate, planet.MinimumRelicTier))
        ];

        decimal averageRelic = team.Length == 0 ? 0m : team.Average(candidate => candidate.Unit.RelicTier);
        decimal averageSpeed = team
            .Where(candidate => candidate.Unit.Stats?.Speed is not null)
            .Select(candidate => candidate.Unit.Stats!.Speed!.Value)
            .DefaultIfEmpty(0m)
            .Average();
        decimal score = (readyUnits * 20m) + (averageRelic * 4m) + Math.Min(20m, averageSpeed / 20m);
        string rationale = ready
            ? $"Equipo completo a R{planet.MinimumRelicTier}+; candidato directo para {planet.Name}."
            : $"{readyUnits}/{TeamSize} unidades listas a R{planet.MinimumRelicTier}+; {Math.Max(0, TeamSize - readyUnits)} mejora(s) separan este arquetipo de un equipo completo.";

        return new RiseOfEmpireTeamRecommendation(
            archetype.Name,
            archetype.TagKeyword,
            ready,
            readyUnits,
            TeamSize,
            Math.Round(score, 1),
            view,
            nextUpgrades,
            rationale);
    }

    private static RiseOfEmpireMissionReadiness AnalyzeMission(
        RiseOfEmpireMissionDefinition mission,
        IReadOnlyCollection<RosterCandidate> roster)
    {
        var missing = new List<string>();
        if (!string.IsNullOrWhiteSpace(mission.FactionTagKeyword))
        {
            int ready = roster.Count(candidate =>
                MatchesTag(candidate.Definition, mission.FactionTagKeyword) &&
                IsMissionReady(candidate.Unit, mission.MinimumRelicTier));
            if (ready < mission.MinimumFactionUnits)
            {
                missing.Add($"faltan {mission.MinimumFactionUnits - ready} unidad(es) de facción a R{mission.MinimumRelicTier}+");
            }
        }

        foreach (RiseOfEmpireUnitRequirementDefinition requirement in mission.UnitRequirements)
        {
            RosterCandidate? candidate = roster
                .Where(item => MatchesAnyAlias(item, requirement.Aliases))
                .OrderByDescending(item => item.Unit.RelicTier)
                .ThenByDescending(item => item.Unit.GalacticPower)
                .FirstOrDefault();
            if (candidate is null)
            {
                missing.Add($"{requirement.Label}: no disponible");
            }
            else if (!IsMissionReady(candidate.Unit, mission.MinimumRelicTier))
            {
                missing.Add($"{requirement.Label}: R{candidate.Unit.RelicTier} → R{mission.MinimumRelicTier}");
            }
        }

        return new RiseOfEmpireMissionReadiness(
            mission.Name,
            mission.Type,
            mission.Requirement,
            mission.MinimumRelicTier,
            missing.Count == 0,
            missing);
    }

    private static IReadOnlyCollection<RiseOfEmpireUpgradePriority> BuildUpgradePriorities(
        IReadOnlyCollection<RiseOfEmpirePlanetAnalysis> planets)
    {
        Dictionary<string, UpgradeAccumulator> candidates = new(StringComparer.OrdinalIgnoreCase);
        foreach (RiseOfEmpirePlanetAnalysis planet in planets)
        {
            int phase = RiseOfEmpireCatalog.Planets.First(definition => definition.Id == planet.Id).Phase;
            foreach (RiseOfEmpireTeamRecommendation team in planet.RecommendedTeams.Where(team => !team.Ready))
            {
                decimal contextValue = team.ReadyUnits == TeamSize - 1 ? 5m : team.ReadyUnits >= 3 ? 2.5m : 1m;
                foreach (RiseOfEmpireUnit unit in team.NextUpgrades)
                {
                    if (unit.RelicsMissing <= 0)
                    {
                        continue;
                    }

                    candidates.TryGetValue(unit.DefinitionId, out UpgradeAccumulator? current);
                    current ??= new UpgradeAccumulator(unit);
                    current.TargetRelicTier = Math.Min(current.TargetRelicTier, planet.MinimumRelicTier);
                    current.UnlockValue += team.ReadyUnits == TeamSize - 1 ? 3 : 1;
                    current.Score += (contextValue * (7 - phase + 1) * 20m) / Math.Max(1, unit.RelicsMissing);
                    current.Planets.Add(planet.Name);
                    candidates[unit.DefinitionId] = current;
                }
            }
        }

        return
        [
            .. candidates.Values
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Unit.RelicsMissing)
                .ThenByDescending(candidate => candidate.Unit.GalacticPower)
                .Take(12)
                .Select((candidate, index) => new RiseOfEmpireUpgradePriority(
                    index + 1,
                    candidate.Unit.DefinitionId,
                    candidate.Unit.Name,
                    candidate.Unit.ThumbnailName,
                    candidate.Unit.RelicTier,
                    candidate.TargetRelicTier,
                    Math.Max(0, candidate.TargetRelicTier - candidate.Unit.RelicTier),
                    candidate.UnlockValue,
                    Math.Round(candidate.Score, 1),
                    [.. candidate.Planets.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)],
                    candidate.UnlockValue >= 3
                        ? "Una sola mejora puede completar al menos un equipo recomendado."
                        : "Mejora reutilizable en varios planetas o arquetipos de RotE."))
        ];
    }

    private static RosterCandidate? ToCandidate(RosterUnit unit, GameDataCatalog catalog) =>
        catalog.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition) && !definition.IsShip
            ? new RosterCandidate(unit, definition)
            : null;

    private static RiseOfEmpireUnit ToView(RosterCandidate candidate, int targetRelic) => new(
        candidate.Unit.DefinitionId,
        candidate.Definition.Name,
        candidate.Definition.ThumbnailName,
        candidate.Unit.RelicTier,
        candidate.Unit.GalacticPower,
        candidate.Unit.Stats?.Speed,
        RelicsMissing(candidate.Unit, targetRelic));

    private static bool IsMissionReady(RosterUnit unit, int minimumRelicTier) =>
        unit.Rarity >= 7 && unit.RelicTier >= minimumRelicTier;

    private static int RelicsMissing(RosterUnit unit, int targetRelic) =>
        Math.Max(0, targetRelic - unit.RelicTier);

    private static bool MatchesTag(GameUnitDefinition definition, string keyword)
    {
        string normalized = Normalize(keyword);
        return definition.Tags.Any(tag => Normalize(tag).Contains(normalized, StringComparison.Ordinal)) ||
            definition.Factions.Any(faction => Normalize(faction).Contains(normalized, StringComparison.Ordinal));
    }

    private static bool MatchesAnyAlias(
        RosterCandidate candidate,
        IReadOnlyCollection<string> aliases)
    {
        string definitionId = Normalize(candidate.Unit.DefinitionId);
        string name = Normalize(candidate.Definition.Name);
        return aliases.Any(alias =>
        {
            string normalized = Normalize(alias);
            return definitionId.Contains(normalized, StringComparison.Ordinal) ||
                name.Contains(normalized, StringComparison.Ordinal);
        });
    }

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark || !char.IsLetterOrDigit(character))
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    private sealed record RosterCandidate(RosterUnit Unit, GameUnitDefinition Definition);

    private sealed class UpgradeAccumulator(RiseOfEmpireUnit unit)
    {
        public RiseOfEmpireUnit Unit { get; } = unit;
        public int TargetRelicTier { get; set; } = int.MaxValue;
        public int UnlockValue { get; set; }
        public decimal Score { get; set; }
        public HashSet<string> Planets { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
