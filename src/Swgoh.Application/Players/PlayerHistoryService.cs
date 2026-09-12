namespace Swgoh.Application.Players;

internal sealed class PlayerHistoryService(IPlayerSnapshotRepository repository) : IPlayerHistoryService
{
    public Task<IReadOnlyCollection<PlayerSnapshot>> GetRecentAsync(
        long allyCode,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        int safeLimit = Math.Clamp(limit, 1, 365);
        return repository.GetRecentAsync(allyCode, safeLimit, cancellationToken);
    }
}
