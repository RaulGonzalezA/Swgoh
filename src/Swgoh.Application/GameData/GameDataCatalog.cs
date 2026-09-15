namespace Swgoh.Application.GameData;

public sealed record GameDataCatalog(
    IReadOnlyDictionary<string, GameUnitDefinition> Units,
    IReadOnlyDictionary<string, GameSkillDefinition> Skills,
    IReadOnlyCollection<GalacticLegendDefinition> GalacticLegends,
    IReadOnlyDictionary<string, GameDatacronSetDefinition>? DatacronSets = null)
{
    public IReadOnlyDictionary<string, GameDatacronSetDefinition> KnownDatacronSets =>
        DatacronSets ?? EmptyDatacronSets;

    private static readonly IReadOnlyDictionary<string, GameDatacronSetDefinition> EmptyDatacronSets =
        new Dictionary<string, GameDatacronSetDefinition>(StringComparer.OrdinalIgnoreCase);
}

public sealed record GameUnitDefinition(
    string BaseId,
    bool IsShip,
    string? NameKey,
    string Name,
    string? ThumbnailName,
    IReadOnlyCollection<string> Factions,
    IReadOnlyCollection<string> Tags);

public sealed record GameSkillDefinition(string Id, int? ZetaTier, int? OmicronTier);

public sealed record GameDatacronSetDefinition(
    string Id,
    DateTimeOffset? ExpiresAtUtc);

public sealed record GalacticLegendDefinition(
    string UnitBaseId,
    string RequirementId,
    IReadOnlyCollection<GalacticLegendUnitRequirement> Requirements);

public sealed record GalacticLegendUnitRequirement(
    string UnitBaseId,
    int MinimumRarity,
    int MinimumGearTier,
    int MinimumRelicTier);
