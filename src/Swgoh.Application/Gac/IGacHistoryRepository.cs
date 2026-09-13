using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacHistoryRepository
{
    Task UpsertManyAsync(
        IReadOnlyCollection<GacHistoricalRound> rounds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<GacHistoricalRound>> GetAsync(
        long allyCode,
        GacFormat? format,
        int maxRounds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<GacHistoricalRound>> GetRecentAsync(
        GacFormat format,
        int maxRounds,
        CancellationToken cancellationToken = default);
}
