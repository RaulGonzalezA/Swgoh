namespace Swgoh.Application.Players;

public interface IGalacticLegendProgressService
{
    Task<IReadOnlyCollection<GalacticLegendProgress>?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default);
}
