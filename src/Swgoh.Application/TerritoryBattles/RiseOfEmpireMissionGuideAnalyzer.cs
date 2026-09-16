using System.Globalization;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireMissionGuideAnalyzer
{
    public static IReadOnlyDictionary<string, IReadOnlyCollection<RiseOfEmpireMissionGuide>> Analyze(
        IReadOnlyCollection<RosterUnit> roster,
        GameDataCatalog catalog)
    {
        Candidate[] candidates =
        [
            .. roster
                .Select(unit => ToCandidate(unit, catalog))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
        ];

        return RiseOfEmpireMissionGuideCatalog.ByPlanet.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<RiseOfEmpireMissionGuide>)[
                .. pair.Value.Select(definition => AnalyzeMission(definition, candidates))
            ],
            StringComparer.OrdinalIgnoreCase);
    }

    private static RiseOfEmpireMissionGuide AnalyzeMission(
        RiseOfEmpireMissionGuideDefinition definition,
        IReadOnlyCollection<Candidate> roster)
    {
        string[] missingRequirements =
        [
            .. definition.RequiredUnits
                .Select(requirement => DescribeMissing(Find(roster, requirement), requirement))
                .Where(message => message is not null)
                .Select(message => message!)
        ];

        RiseOfEmpireConcreteTeamRecommendation[] teams =
        [
            .. definition.RecommendedTeams
                .Select(team => AnalyzeTeam(team, roster))
                .OrderByDescending(team => team.Ready)
                .ThenByDescending(team => team.ReadyUnits)
                .ThenBy(team => ConfidenceOrder(team.Confidence))
        ];

        return new RiseOfEmpireMissionGuide(
            definition.Id,
            definition.Name,
            definition.Type,
            definition.Requirement,
            definition.IsFleet,
            definition.MinimumRelicTier,
            missingRequirements.Length == 0,
            missingRequirements,
            teams);
    }

    private static RiseOfEmpireConcreteTeamRecommendation AnalyzeTeam(
        RiseOfEmpireConcreteTeamDefinition definition,
        IReadOnlyCollection<Candidate> roster)
    {
        var units = new List<RiseOfEmpireGuideUnit>(definition.Units.Count);
        var missing = new List<string>();
        int ready = 0;

        foreach (RiseOfEmpireGuideUnitDefinition requirement in definition.Units)
        {
            Candidate? candidate = Find(roster, requirement);
            string? missingMessage = DescribeMissing(candidate, requirement);
            if (candidate is null)
            {
                missing.Add(missingMessage!);
                continue;
            }

            bool isReady = missingMessage is null;
            if (isReady)
            {
                ready++;
            }
            else
            {
                missing.Add(missingMessage!);
            }

            units.Add(new RiseOfEmpireGuideUnit(
                candidate.Unit.DefinitionId,
                candidate.Definition.Name,
                candidate.Definition.ThumbnailName,
                candidate.Unit.IsShip,
                candidate.Unit.Rarity,
                candidate.Unit.RelicTier,
                candidate.Unit.GalacticPower,
                isReady,
                RequirementLabel(requirement)));
        }

        return new RiseOfEmpireConcreteTeamRecommendation(
            definition.Name,
            definition.Confidence,
            ready == definition.Units.Count,
            ready,
            definition.Units.Count,
            units,
            missing,
            definition.Rationale);
    }

    private static Candidate? Find(
        IReadOnlyCollection<Candidate> roster,
        RiseOfEmpireGuideUnitDefinition requirement) => roster
        .Where(candidate => candidate.Unit.IsShip == requirement.IsShip && Matches(candidate, requirement.Aliases))
        .OrderByDescending(candidate => IsReady(candidate.Unit, requirement))
        .ThenByDescending(candidate => candidate.Unit.Rarity)
        .ThenByDescending(candidate => candidate.Unit.RelicTier)
        .ThenByDescending(candidate => candidate.Unit.GalacticPower)
        .FirstOrDefault();

    private static bool Matches(Candidate candidate, IReadOnlyCollection<string> aliases)
    {
        string definitionId = Normalize(candidate.Unit.DefinitionId);
        string name = Normalize(candidate.Definition.Name);
        return aliases.Any(alias =>
        {
            string normalized = Normalize(alias);
            return string.Equals(definitionId, normalized, StringComparison.Ordinal) ||
                string.Equals(name, normalized, StringComparison.Ordinal);
        });
    }

    private static string? DescribeMissing(Candidate? candidate, RiseOfEmpireGuideUnitDefinition requirement)
    {
        if (candidate is null)
        {
            return $"{requirement.Label}: no disponible";
        }

        if (candidate.Unit.Rarity < requirement.MinimumRarity)
        {
            return $"{requirement.Label}: {candidate.Unit.Rarity}★ → {requirement.MinimumRarity}★";
        }

        if (!requirement.IsShip && candidate.Unit.RelicTier < requirement.MinimumRelicTier)
        {
            return $"{requirement.Label}: R{candidate.Unit.RelicTier} → R{requirement.MinimumRelicTier}";
        }

        return null;
    }

    private static bool IsReady(RosterUnit unit, RiseOfEmpireGuideUnitDefinition requirement) =>
        unit.Rarity >= requirement.MinimumRarity &&
        (requirement.IsShip || unit.RelicTier >= requirement.MinimumRelicTier);

    private static string RequirementLabel(RiseOfEmpireGuideUnitDefinition requirement) =>
        requirement.IsShip
            ? $"{requirement.MinimumRarity}★"
            : $"R{requirement.MinimumRelicTier}+";

    private static int ConfidenceOrder(string confidence) => confidence switch
    {
        "Alta" => 0,
        "Media" => 1,
        _ => 2
    };

    private static Candidate? ToCandidate(RosterUnit unit, GameDataCatalog catalog) =>
        catalog.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition)
            ? new Candidate(unit, definition)
            : null;

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

    private sealed record Candidate(RosterUnit Unit, GameUnitDefinition Definition);
}
