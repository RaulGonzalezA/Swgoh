namespace Swgoh.Application.Players;

public interface IPlayerRosterService
{
    Task<PlayerRosterPage?> GetAsync(
        long allyCode,
        PlayerRosterQuery query,
        CancellationToken cancellationToken = default);

    Task<PlayerRosterSnapshot?> GetSnapshotAsync(
        long allyCode,
        CancellationToken cancellationToken = default);
}
