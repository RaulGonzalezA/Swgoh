using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface ICurrentGacOpponentSource
{
    Task<CurrentGacOpponentLookup> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken = default);
}
