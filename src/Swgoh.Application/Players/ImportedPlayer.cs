namespace Swgoh.Application.Players;

public sealed record ImportedPlayer(
    long AllyCode,
    string PlayerId,
    string Name,
    string? GuildId,
    string? GuildName,
    int Level,
    long GalacticPower,
    IReadOnlyCollection<ImportedRosterUnit> Roster);

public sealed record ImportedRosterUnit(
    string Id,
    string DefinitionId,
    int Level,
    int Rarity,
    int GearTier,
    int RelicTier,
    int EquippedModCount);
