using System.Globalization;
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

        HashSet<string> expectedIds = GetExpectedIds(roster);

        using HttpResponseMessage response = await httpClient
            .PostAsJsonAsync("api?flags=onlyGP", roster, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw InvalidProviderData("SWGOH Stats returned invalid JSON.", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw InvalidProviderData("SWGOH Stats response must be a JSON array.");
            }

            Dictionary<string, long> result = new(StringComparer.Ordinal);
            foreach (JsonElement unit in document.RootElement.EnumerateArray())
            {
                string id = ReadRequiredId(unit);
                if (!expectedIds.Contains(id))
                {
                    throw InvalidProviderData($"SWGOH Stats returned unexpected roster unit '{id}'.");
                }

                if (!result.TryAdd(id, ReadRequiredGalacticPower(unit, id)))
                {
                    throw InvalidProviderData($"SWGOH Stats returned duplicate roster unit '{id}'.");
                }
            }

            string[] missingIds = [.. expectedIds.Where(id => !result.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal)];
            if (missingIds.Length > 0)
            {
                throw InvalidProviderData(
                    $"SWGOH Stats did not return Galactic Power for roster unit(s): {string.Join(", ", missingIds)}.");
            }

            return result;
        }
    }

    private static HashSet<string> GetExpectedIds(IEnumerable<JsonElement> roster)
    {
        HashSet<string> result = new(StringComparer.Ordinal);
        foreach (JsonElement unit in roster)
        {
            string id = ReadRequiredId(unit, "Comlink roster");
            if (!result.Add(id))
            {
                throw InvalidProviderData($"Comlink roster contains duplicate roster unit '{id}'.");
            }
        }

        return result;
    }

    private static string ReadRequiredId(JsonElement unit, string provider = "SWGOH Stats")
    {
        if (!unit.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            throw InvalidProviderData($"{provider} returned a roster unit without a valid id.");
        }

        return idElement.GetString()!.Trim();
    }

    private static long ReadRequiredGalacticPower(JsonElement unit, string id)
    {
        if (!unit.TryGetProperty("gp", out JsonElement gpElement))
        {
            throw InvalidProviderData($"SWGOH Stats returned roster unit '{id}' without Galactic Power.");
        }

        long galacticPower;
        if (gpElement.ValueKind == JsonValueKind.Number)
        {
            if (!gpElement.TryGetInt64(out galacticPower))
            {
                throw InvalidProviderData($"SWGOH Stats returned invalid Galactic Power for roster unit '{id}'.");
            }
        }
        else if (gpElement.ValueKind == JsonValueKind.String
            && long.TryParse(
                gpElement.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed))
        {
            galacticPower = parsed;
        }
        else
        {
            throw InvalidProviderData($"SWGOH Stats returned invalid Galactic Power for roster unit '{id}'.");
        }

        if (galacticPower < 0)
        {
            throw InvalidProviderData($"SWGOH Stats returned negative Galactic Power for roster unit '{id}'.");
        }

        return galacticPower;
    }

    private static HttpRequestException InvalidProviderData(string message, Exception? innerException = null) =>
        new(message, innerException);
}
