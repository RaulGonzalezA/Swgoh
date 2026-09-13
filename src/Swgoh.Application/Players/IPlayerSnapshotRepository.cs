namespace Swgoh.Application.Players;

public interface IPlayerSnapshotRepository
{
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);

    Task UpsertAsync(PlayerSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PlayerSnapshot>> GetRecentAsync(long allyCode, int limit, CancellationToken cancellationToken = default);
}
