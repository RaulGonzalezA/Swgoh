namespace Swgoh.Application.Players;

public interface IPlayerAnalysisService
{
    Task<PlayerRosterAnalysis?> GetAsync(long allyCode, CancellationToken cancellationToken = default);
}
