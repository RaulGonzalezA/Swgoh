namespace Swgoh.Application.GameData;

public sealed record DatacronSetExpiration(
    string SetId,
    DateTimeOffset? ExpiresAtUtc);

public interface IDatacronSetCatalog
{
    Task<IReadOnlyDictionary<string, DatacronSetExpiration>> GetAsync(
        CancellationToken cancellationToken = default);
}
