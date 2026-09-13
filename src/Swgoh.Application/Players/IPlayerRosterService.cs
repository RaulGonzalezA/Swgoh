namespace Swgoh.Application.Players;

public interface IPlayerRosterService
{
    Task<PlayerRosterPage?> GetAsync(
        long allyCode,
        PlayerRosterQuery query,
        CancellationToken cancellationToken = default);
}
