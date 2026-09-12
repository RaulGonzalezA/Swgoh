namespace Swgoh.Application.Players;

public interface ISwgohPlayerClient
{
    Task<ImportedPlayer> GetPlayerAsync(long allyCode, CancellationToken cancellationToken = default);
}
