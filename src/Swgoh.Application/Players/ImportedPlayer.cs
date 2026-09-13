using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

public sealed record ImportedPlayer(
    long AllyCode,
    string PlayerId,
    string Name,
    string? GuildId,
    string? GuildName,
    int Level,
    long GalacticPower,
    IReadOnlyCollection<ImportedRosterUnit> Roster,
    IReadOnlyCollection<PlayerDatacron>? Datacrons = null);

public sealed record ImportedRosterUnit(
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
