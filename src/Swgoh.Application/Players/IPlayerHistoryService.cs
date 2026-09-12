namespace Swgoh.Application.Players;

public interface IPlayerHistoryService
{
    Task<IReadOnlyCollection<PlayerSnapshot>> GetRecentAsync(long allyCode, int limit = 30, CancellationToken cancellationToken = default);
}
