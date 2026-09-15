namespace Swgoh.Application.GameData;

public sealed record GameDataCatalog(
    IReadOnlyDictionary<string, GameUnitDefinition> Units,
    IReadOnlyDictionary<string, GameSkillDefinition> Skills,
    IReadOnlyCollection<GalacticLegendDefinition> GalacticLegends);

public sealed record GameUnitDefinition(
    string BaseId,
    bool IsShip,
    string? NameKey,
    string Name,
    string? ThumbnailName,
    IReadOnlyCollection<string> Factions,
    IReadOnlyCollection<string> Tags);

public sealed record GameSkillDefinition(string Id, int? ZetaTier, int? OmicronTier);

public sealed record GalacticLegendDefinition(
    string UnitBaseId,
    string RequirementId,
    IReadOnlyCollection<GalacticLegendUnitRequirement> Requirements);

public sealed record GalacticLegendUnitRequirement(
    string UnitBaseId,
    int MinimumRarity,
    int MinimumGearTier,
    int MinimumRelicTier);
