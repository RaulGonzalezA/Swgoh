namespace Swgoh.Application.Players;

public sealed record GalacticLegendRequirementProgress(
    string UnitBaseId,
    bool Owned,
    int CurrentRarity,
    int CurrentGearTier,
    int CurrentRelicTier,
    int MinimumRarity,
    int MinimumGearTier,
    int MinimumRelicTier,
    bool Complete);

public sealed record GalacticLegendProgress(
    string UnitBaseId,
    bool Unlocked,
    int CompletedRequirements,
    int TotalRequirements,
    decimal CompletionPercent,
    IReadOnlyCollection<GalacticLegendRequirementProgress> Requirements);
