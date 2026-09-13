using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacPersonalOutcomeRepository
{
    Task UpsertAsync(
        GacPersonalRoundOutcome outcome,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<GacPersonalRoundOutcome>> GetRecentAsync(
        long allyCode,
        GacFormat format,
        int limit,
        CancellationToken cancellationToken = default);
}
