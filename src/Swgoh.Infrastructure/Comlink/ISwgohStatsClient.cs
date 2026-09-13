using System.Text.Json;

using Swgoh.Domain.Players;

namespace Swgoh.Infrastructure.Comlink;

internal interface ISwgohStatsClient
{
    Task<IReadOnlyDictionary<string, CalculatedRosterUnitStats>> CalculateRosterStatsAsync(
        IReadOnlyCollection<JsonElement> roster,
        CancellationToken cancellationToken = default);
}

internal sealed record CalculatedRosterUnitStats(
    long GalacticPower,
    RosterUnitStats? Stats);
