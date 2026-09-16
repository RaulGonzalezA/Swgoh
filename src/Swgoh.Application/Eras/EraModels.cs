namespace Swgoh.Application.Eras;

public sealed record EraAnalysis(
    long AllyCode,
    string PlayerName,
    DateTimeOffset RosterUpdatedAtUtc,
    string CatalogVersion,
    string EraId,
    string EraName,
    DateOnly StartedOn,
    IReadOnlyCollection<EraUnitStatus> Units,
    EraJourneyProgress Journey,
    ColiseumAnalysis Coliseum)
{
    public int OwnedUnits => Units.Count(unit => unit.Owned);
    public int TotalUnits => Units.Count;
}

public sealed record EraUnitStatus(
    string Key,
    string Name,
    string Alignment,
    string Role,
    IReadOnlyCollection<string> Categories,
    bool IsJourneyUnit,
    bool Owned,
    string? DefinitionId,
    string? ThumbnailName,
    int Stars,
    int RelicTier,
    long GalacticPower,
    string PrimarySynergy,
    string Notes);

public sealed record EraJourneyProgress(
    string UnitName,
    IReadOnlyCollection<EraJourneyTierProgress> Tiers)
{
    public int StarReadyTiers => Tiers.Count(tier => tier.StarRequirementsMet);
}

public sealed record EraJourneyTierProgress(
    int Tier,
    int RequiredStars,
    bool StarRequirementsMet,
    IReadOnlyCollection<string> RequiredUnits,
    IReadOnlyCollection<string> MissingStarRequirements,
    IReadOnlyCollection<EraLevelRequirement> EraLevelRequirements,
    string RewardSummary);

public sealed record EraLevelRequirement(string UnitName, int EraLevel);

public sealed record ColiseumAnalysis(
    int MaxTier,
    int OwnedEraUnits,
    IReadOnlyCollection<ColiseumBoss> Bosses,
    IReadOnlyCollection<ColiseumTierGuidance> TierGuidance,
    IReadOnlyCollection<string> GeneralGuidance,
    bool EraLevelIsAvailableFromRoster);

public sealed record ColiseumBoss(
    string Id,
    string Name,
    string RotationNote,
    string StrategyNote);

public sealed record ColiseumTierGuidance(
    int Tier,
    int? RecommendedEraLevel,
    string Note);
