using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacHistoryService
{
    Task<GacHistoryImportResult> ImportAsync(
        long allyCode,
        IReadOnlyCollection<GacHistoryRoundInput> rounds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<GacHistoricalRound>> GetAsync(
        long allyCode,
        GacHistoryQuery query,
        CancellationToken cancellationToken = default);
}
