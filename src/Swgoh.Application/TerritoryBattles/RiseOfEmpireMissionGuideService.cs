using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

public interface IRiseOfEmpireMissionGuideService
{
    Task<RiseOfEmpireMissionGuideAnalysis?> GetAsync(long allyCode, CancellationToken cancellationToken = default);
}

internal sealed class RiseOfEmpireMissionGuideService(
    IPlayerProfileService playerProfileService,
    ISwgohGameDataCatalog gameDataCatalog) : IRiseOfEmpireMissionGuideService
{
    public async Task<RiseOfEmpireMissionGuideAnalysis?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(allyCode, cancellationToken);
        Task<GameDataCatalog> catalogTask = gameDataCatalog.GetAsync(cancellationToken);
        await Task.WhenAll(playerTask, catalogTask).ConfigureAwait(false);

        PlayerProfile? player = await playerTask.ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        GameDataCatalog catalog = await catalogTask.ConfigureAwait(false);
        IReadOnlyDictionary<string, IReadOnlyCollection<RiseOfEmpireMissionGuide>> guides =
            RiseOfEmpireMissionGuideAnalyzer.Analyze(player.Roster, catalog);

        RiseOfEmpirePlanetMissionGuides[] planets =
        [
            .. RiseOfEmpireCatalog.Planets
                .Where(planet => guides.ContainsKey(planet.Id))
                .OrderBy(planet => planet.Phase)
                .ThenBy(planet => planet.Name, StringComparer.OrdinalIgnoreCase)
                .Select(planet => new RiseOfEmpirePlanetMissionGuides(
                    planet.Id,
                    planet.Name,
                    planet.Phase,
                    guides[planet.Id]))
        ];

        return new RiseOfEmpireMissionGuideAnalysis(
            player.AllyCode,
            player.Name,
            player.UpdatedAtUtc,
            RiseOfEmpireMissionGuideCatalog.Version,
            planets);
    }
}
