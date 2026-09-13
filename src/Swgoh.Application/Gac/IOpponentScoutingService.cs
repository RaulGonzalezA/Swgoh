using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IOpponentScoutingService
{
    Task<OpponentScoutingReport?> GetAsync(
        long allyCode,
        GacFormat format,
        GacLeague? targetLeague,
        int maxRounds,
        CancellationToken cancellationToken = default);
}
