namespace Swgoh.Application.Investments;

public interface IPlayerInventoryRepository
{
    Task<PlayerInventorySnapshot?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        PlayerInventorySnapshot snapshot,
        CancellationToken cancellationToken = default);
}
