using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacPersonalBattleRepository
{
    Task<GacPersonalBattleObservation?> FindByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetAsync(
        long playerAllyCode,
        GacFormat format,
        int limit = 1_000,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetRoundAsync(
        long playerAllyCode,
        string eventInstanceId,
        int roundNumber,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        GacPersonalBattleObservation observation,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string id,
        CancellationToken cancellationToken = default);
}
