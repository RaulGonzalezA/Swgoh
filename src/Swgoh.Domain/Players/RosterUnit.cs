namespace Swgoh.Domain.Players;

public sealed record RosterUnit(
    string Id,
    string DefinitionId,
    int Level,
    int Rarity,
    int GearTier,
    int RelicTier,
    int EquippedModCount,
    long GalacticPower = 0,
    bool IsShip = false,
    int ZetaCount = 0,
    int OmicronCount = 0,
    RosterUnitStats? Stats = null,
    RosterModSummary? Mods = null);
