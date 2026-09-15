using Microsoft.Extensions.Hosting;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class PersistedGacOpponentSourceAdapter(PersistedGacOpponentSource source) : ICurrentGacOpponentSource
{
    public Task<CurrentGacOpponentLookup> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken = default) =>
        source.GetAsync(allyCode, formatOverride, cancellationToken);
}

internal sealed class PersistedGacOpponentCacheAdapter(PersistedGacOpponentSource source) : ICurrentGacOpponentCache
{
    public void Invalidate(long allyCode) => source.Invalidate(allyCode);
}

internal sealed class BackgroundGacOpponentHostedService(BackgroundGacOpponentSource source) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => source.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => source.StopAsync(cancellationToken);
}
