using System.Net.Http.Json;
using System.Text.Json;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class SwgohStatsClient(HttpClient httpClient) : ISwgohStatsClient
{
    public async Task<IReadOnlyDictionary<string, long>> CalculateGalacticPowerAsync(
        IReadOnlyCollection<JsonElement> roster,
        CancellationToken cancellationToken = default)
    {
        if (roster.Count == 0)
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        using HttpResponseMessage response = await httpClient
            .PostAsJsonAsync("api?flags=onlyGP", roster, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        Dictionary<string, long> result = new(StringComparer.Ordinal);
        foreach (JsonElement unit in document.RootElement.EnumerateArray())
        {
            if (!unit.TryGetProperty("id", out JsonElement idElement))
            {
                continue;
            }

            string? id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id) || !unit.TryGetProperty("gp", out JsonElement gpElement))
            {
                continue;
            }

            long gp = gpElement.ValueKind == JsonValueKind.Number
                ? gpElement.GetInt64()
                : long.TryParse(gpElement.GetString(), out long parsed) ? parsed : 0;
            result[id] = gp;
        }

        return result;
    }
}
