using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using Swgoh.Domain.Players;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class SwgohStatsClient(HttpClient httpClient) : ISwgohStatsClient
{
    public async Task<IReadOnlyDictionary<string, CalculatedRosterUnitStats>> CalculateRosterStatsAsync(
        IReadOnlyCollection<JsonElement> roster,
        CancellationToken cancellationToken = default)
    {
        if (roster.Count == 0)
        {
            return new Dictionary<string, CalculatedRosterUnitStats>(StringComparer.Ordinal);
        }

        HashSet<string> expectedIds = GetExpectedIds(roster);

        using HttpResponseMessage response = await httpClient
            .PostAsJsonAsync("api?flags=gameStyle,calcGP,statIDs", roster, cancellationToken)
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

            Dictionary<string, CalculatedRosterUnitStats> result = new(StringComparer.Ordinal);
            foreach (JsonElement unit in document.RootElement.EnumerateArray())
            {
                string id = ReadRequiredId(unit);
                if (!expectedIds.Contains(id))
                {
                    throw InvalidProviderData($"SWGOH Stats returned unexpected roster unit '{id}'.");
                }

                var calculated = new CalculatedRosterUnitStats(
                    ReadRequiredGalacticPower(unit, id),
                    ReadOptionalStats(unit));
                if (!result.TryAdd(id, calculated))
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

    private static RosterUnitStats? ReadOptionalStats(JsonElement unit)
    {
        if (!unit.TryGetProperty("stats", out JsonElement statsElement)
            || statsElement.ValueKind != JsonValueKind.Object
            || !statsElement.TryGetProperty("final", out JsonElement finalElement)
            || finalElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var stats = new RosterUnitStats(
            Health: ReadOptionalDecimal(finalElement, "1"),
            Protection: ReadOptionalDecimal(finalElement, "28"),
            Speed: ReadOptionalDecimal(finalElement, "5"),
            PhysicalDamage: ReadOptionalDecimal(finalElement, "6"),
            SpecialDamage: ReadOptionalDecimal(finalElement, "7"),
            Armor: ReadOptionalDecimal(finalElement, "8"),
            Resistance: ReadOptionalDecimal(finalElement, "9"),
            Potency: ReadOptionalDecimal(finalElement, "17"),
            Tenacity: ReadOptionalDecimal(finalElement, "18"),
            CriticalDamage: ReadOptionalDecimal(finalElement, "16"));

        return stats.Health is null
            && stats.Protection is null
            && stats.Speed is null
            && stats.PhysicalDamage is null
            && stats.SpecialDamage is null
            && stats.Armor is null
            && stats.Resistance is null
            && stats.Potency is null
            && stats.Tenacity is null
            && stats.CriticalDamage is null
                ? null
                : stats;
    }

    private static decimal? ReadOptionalDecimal(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal numeric))
        {
            return numeric;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
        {
            return parsed;
        }

        return null;
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
