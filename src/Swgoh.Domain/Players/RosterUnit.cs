namespace Swgoh.Domain.Players;

public sealed record RosterUnit(
    string Id,
    string DefinitionId,
    int Level,
    int Rarity,
    int GearTier,
    int RelicTier,
    int EquippedModCount);
