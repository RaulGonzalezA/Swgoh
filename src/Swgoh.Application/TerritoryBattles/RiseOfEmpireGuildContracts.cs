using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

public interface IRiseOfEmpireGuildService
{
    Task<RiseOfEmpireGuildAnalysis?> GetAsync(
        long allyCode,
        bool refreshGuildRoster,
        CancellationToken cancellationToken = default);
}

public interface IRiseOfEmpireGuildSource
{
    Task<RiseOfEmpireGuildSnapshot> GetAsync(
        string guildId,
        CancellationToken cancellationToken = default);
}

public interface IRiseOfEmpireGuildPlayerRepository
{
    Task<IReadOnlyCollection<PlayerProfile>> FindByGuildIdAsync(
        string guildId,
        int maxMembers = 50,
        CancellationToken cancellationToken = default);
}

public interface IRiseOfEmpireOperationsCatalog
{
    Task<IReadOnlyCollection<RiseOfEmpireOperationDefinition>> GetAsync(
        CancellationToken cancellationToken = default);
}

public sealed record RiseOfEmpireOperationDefinition(
    string Id,
    int Phase,
    string PlanetName,
    string Type,
    bool IsBonus,
    long TotalPoints,
    IReadOnlyCollection<RiseOfEmpireOperationSquadDefinition> Squads);

public sealed record RiseOfEmpireOperationSquadDefinition(
    string Id,
    long Points,
    IReadOnlyCollection<RiseOfEmpireOperationUnitDefinition> Units);

public sealed record RiseOfEmpireOperationUnitDefinition(
    string BaseId,
    string Name,
    bool IsShip,
    int RequiredRarity,
    int RequiredRelicTier);
