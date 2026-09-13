namespace Swgoh.Application.Players;

public sealed record PlayerRosterUnit(
    string Id,
    string DefinitionId,
    string Name,
    string? NameKey,
    string? ThumbnailName,
    IReadOnlyCollection<string> Factions,
    IReadOnlyCollection<string> Tags,
    int Level,
    int Rarity,
    int GearTier,
    int RelicTier,
    int EquippedModCount,
    long GalacticPower,
    bool IsShip,
    int ZetaCount,
    int OmicronCount);
