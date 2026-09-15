using System.Globalization;
using System.Text.Json;

using Swgoh.Application.GameData;

namespace Swgoh.Infrastructure.GameData;

internal sealed class SwgohDatacronSetCatalogClient(IHttpClientFactory httpClientFactory) : IDatacronSetCatalog
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private IReadOnlyDictionary<string, DatacronSetExpiration>? cached;
    private DateTimeOffset cacheExpiresAtUtc;

    public async Task<IReadOnlyDictionary<string, DatacronSetExpiration>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (cached is not null && DateTimeOffset.UtcNow < cacheExpiresAtUtc)
        {
            return cached;
        }

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cached is not null && DateTimeOffset.UtcNow < cacheExpiresAtUtc)
            {
                return cached;
            }

            HttpClient client = httpClientFactory.CreateClient(SwgohGameDataCatalogClient.HttpClientName);
            using Stream stream = await client.GetStreamAsync("datacronSet.json", cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            cached = Parse(document.RootElement);
            cacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
            return cached;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    internal static IReadOnlyDictionary<string, DatacronSetExpiration> Parse(JsonElement root)
    {
        var result = new Dictionary<string, DatacronSetExpiration>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement set in EnumerateData(root))
        {
            string? setId = ReadScalarString(set, "id");
            if (string.IsNullOrWhiteSpace(setId))
            {
                continue;
            }

            long? expirationMs = ReadNullableLong(set, "expirationTimeMs");
            DateTimeOffset? expiresAtUtc = expirationMs is > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(expirationMs.Value)
                : null;
            result[setId] = new DatacronSetExpiration(setId, expiresAtUtc);
        }

        return result;
    }

    private static IEnumerable<JsonElement> EnumerateData(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToArray();
        }

        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out JsonElement data)
            && data.ValueKind == JsonValueKind.Array
                ? data.EnumerateArray().ToArray()
                : [];
    }

    private static string? ReadScalarString(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static long? ReadNullableLong(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : null;
    }
}
