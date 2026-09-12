using System.Text.Json;

namespace Swgoh.Infrastructure.Comlink;

internal interface ISwgohStatsClient
{
    Task<IReadOnlyDictionary<string, long>> CalculateGalacticPowerAsync(
        IReadOnlyCollection<JsonElement> roster,
        CancellationToken cancellationToken = default);
}
