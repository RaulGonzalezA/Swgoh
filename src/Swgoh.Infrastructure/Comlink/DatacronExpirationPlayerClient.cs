using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class DatacronExpirationPlayerClient(
    SwgohComlinkClient inner,
    IDatacronSetCatalog datacronSetCatalog) : ISwgohPlayerClient
{
    public async Task<ImportedPlayer> GetPlayerAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        Task<ImportedPlayer> playerTask = inner.GetPlayerAsync(allyCode, cancellationToken);
        Task<IReadOnlyDictionary<string, DatacronSetExpiration>> setsTask =
            datacronSetCatalog.GetAsync(cancellationToken);
        await Task.WhenAll(playerTask, setsTask).ConfigureAwait(false);

        ImportedPlayer player = await playerTask.ConfigureAwait(false);
        IReadOnlyDictionary<string, DatacronSetExpiration> sets = await setsTask.ConfigureAwait(false);
        PlayerDatacron[] datacrons =
        [
            .. (player.Datacrons ?? []).Select(datacron =>
                sets.TryGetValue(datacron.SetId, out DatacronSetExpiration? set)
                    ? datacron with { ExpiresAtUtc = set.ExpiresAtUtc }
                    : datacron)
        ];

        return player with { Datacrons = datacrons };
    }
}
