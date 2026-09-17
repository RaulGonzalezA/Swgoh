namespace Swgoh.Application.Investments;

public sealed record InvestmentTarget(
    long AllyCode,
    string DefinitionId,
    int? TargetRelicTier,
    int? TargetStars,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record InvestmentTargetUpdate(
    int? TargetRelicTier,
    int? TargetStars);

public sealed record InvestmentTargetProgress(
    long AllyCode,
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    bool IsShip,
    int CurrentRelicTier,
    int CurrentStars,
    int? TargetRelicTier,
    int? TargetStars,
    bool Completed,
    decimal Progress,
    int RelicStepsRemaining,
    int StarStepsRemaining,
    string SuggestedAction,
    InvestmentInventoryFit? Inventory,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
