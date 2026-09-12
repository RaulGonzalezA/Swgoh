namespace Swgoh.Application.Players;

public interface IPlayerSnapshotRepository
{
    Task UpsertAsync(PlayerSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PlayerSnapshot>> GetRecentAsync(long allyCode, int limit, CancellationToken cancellationToken = default);
}
