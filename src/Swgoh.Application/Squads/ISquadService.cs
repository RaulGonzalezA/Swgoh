namespace Swgoh.Application.Squads;

public interface ISquadService
{
    Task<SquadDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SquadDetails>> SearchAsync(
        SquadSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<SquadDetails> CreateAsync(
        SaveSquadDefinition input,
        CancellationToken cancellationToken = default);

    Task<SquadDetails?> UpdateAsync(
        Guid id,
        SaveSquadDefinition input,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
