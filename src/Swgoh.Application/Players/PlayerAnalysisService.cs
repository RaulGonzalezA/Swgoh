using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

internal sealed class PlayerAnalysisService(IPlayerRepository repository) : IPlayerAnalysisService
{
    public async Task<PlayerRosterAnalysis?> GetAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        PlayerProfile? player = await repository.FindByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return player is null ? null : PlayerRosterMetrics.Calculate(player);
    }
}
