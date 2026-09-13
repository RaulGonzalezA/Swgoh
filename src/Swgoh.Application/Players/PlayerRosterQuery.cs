namespace Swgoh.Application.Players;

public sealed record PlayerRosterQuery(
    int Page = 1,
    int PageSize = 50,
    string? Search = null,
    PlayerRosterUnitType Type = PlayerRosterUnitType.All,
    int? MinRarity = null,
    int? MinRelic = null,
    bool? HasZeta = null,
    bool? HasOmicron = null,
    PlayerRosterSortField OrderBy = PlayerRosterSortField.GalacticPower,
    PlayerRosterSortDirection Direction = PlayerRosterSortDirection.Descending);

public enum PlayerRosterUnitType
{
    All,
    Character,
    Ship
}

public enum PlayerRosterSortField
{
    GalacticPower,
    RelicTier,
    Rarity,
    GearTier,
    Level,
    DefinitionId,
    Name
}

public enum PlayerRosterSortDirection
{
    Ascending,
    Descending
}
