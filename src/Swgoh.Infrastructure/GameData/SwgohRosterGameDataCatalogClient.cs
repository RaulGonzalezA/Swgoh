using System.IO.Compression;
using System.Text.Json;

using Swgoh.Application.GameData;

namespace Swgoh.Infrastructure.GameData;

internal sealed class SwgohRosterGameDataCatalogClient : IRosterGameDataCatalog
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly IHttpClientFactory httpClientFactory;
    private readonly string locale;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private IReadOnlyDictionary<string, GameUnitDefinition>? cachedUnits;
    private DateTimeOffset cacheExpiresAtUtc;

    public SwgohRosterGameDataCatalogClient(IHttpClientFactory httpClientFactory, string locale = "SPA_XM")
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        this.httpClientFactory = httpClientFactory;
        this.locale = string.IsNullOrWhiteSpace(locale) ? "SPA_XM" : locale.Trim();
    }

    public async Task<IReadOnlyDictionary<string, GameUnitDefinition>> GetUnitsAsync(
        CancellationToken cancellationToken = default)
    {
        if (cachedUnits is not null && DateTimeOffset.UtcNow < cacheExpiresAtUtc)
        {
            return cachedUnits;
        }

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cachedUnits is not null && DateTimeOffset.UtcNow < cacheExpiresAtUtc)
            {
                return cachedUnits;
            }

            HttpClient httpClient = httpClientFactory.CreateClient(SwgohGameDataCatalogClient.HttpClientName);
            Task<JsonDocument> unitsTask = GetJsonAsync(httpClient, "units_gas.json", cancellationToken);
            Task<JsonDocument> categoriesTask = GetJsonAsync(httpClient, "category.json", cancellationToken);
            Task<JsonDocument> localizationTask = GetBrotliJsonAsync(
                httpClient,
                $"Loc_{locale}.txt.json.br",
                cancellationToken);

            await Task.WhenAll(unitsTask, categoriesTask, localizationTask).ConfigureAwait(false);

            using JsonDocument unitsDocument = await unitsTask.ConfigureAwait(false);
            using JsonDocument categoriesDocument = await categoriesTask.ConfigureAwait(false);
            using JsonDocument localizationDocument = await localizationTask.ConfigureAwait(false);

            IReadOnlyDictionary<string, string> localization = ParseLocalization(localizationDocument.RootElement);
            IReadOnlyDictionary<string, CategoryDefinition> categories = ParseCategories(categoriesDocument.RootElement);
            cachedUnits = ParseUnits(unitsDocument.RootElement, categories, localization);
            cacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
            return cachedUnits;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(
        HttpClient httpClient,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using Stream stream = await httpClient.GetStreamAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> GetBrotliJsonAsync(
        HttpClient httpClient,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            relativeUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        byte[] payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (LooksLikeJson(payload))
        {
            return JsonDocument.Parse(payload);
        }

        using var compressed = new MemoryStream(payload, writable: false);
        using var brotli = new BrotliStream(compressed, CompressionMode.Decompress);
        return await JsonDocument.ParseAsync(brotli, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static bool LooksLikeJson(ReadOnlySpan<byte> payload)
    {
        foreach (byte value in payload)
        {
            if (value is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
            {
                continue;
            }

            return value is (byte)'{' or (byte)'[';
        }

        return false;
    }

    private static Dictionary<string, GameUnitDefinition> ParseUnits(
        JsonElement root,
        IReadOnlyDictionary<string, CategoryDefinition> categories,
        IReadOnlyDictionary<string, string> localization)
    {
        Dictionary<string, GameUnitDefinition> result = new(StringComparer.Ordinal);
        foreach (JsonElement unit in EnumerateData(root))
        {
            string? baseId = GetString(unit, "baseId") ?? GetString(unit, "id");
            if (string.IsNullOrWhiteSpace(baseId))
            {
                continue;
            }

            string? nameKey = GetString(unit, "nameKey");
            string name = ResolveText(localization, nameKey) ?? baseId;
            string? thumbnailName = GetString(unit, "thumbnailName");
            string[] tags = GetStringArray(unit, "categoryId");
            string[] factions =
            [
                .. tags
                    .Where(tag => IsVisibleFactionCategory(tag, categories))
                    .Select(tag => categories.TryGetValue(tag, out CategoryDefinition? category)
                        ? ResolveText(localization, category.DescriptionKey) ?? FormatCategoryId(tag)
                        : FormatCategoryId(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            ];

            result[baseId] = new GameUnitDefinition(
                baseId,
                IsShip(unit),
                nameKey,
                name,
                thumbnailName,
                factions,
                tags);
        }

        return result;
    }

    private static Dictionary<string, CategoryDefinition> ParseCategories(JsonElement root)
    {
        Dictionary<string, CategoryDefinition> result = new(StringComparer.Ordinal);
        foreach (JsonElement category in EnumerateData(root))
        {
            string? id = GetString(category, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result[id] = new CategoryDefinition(
                GetString(category, "descKey"),
                GetBoolean(category, "visible"));
        }

        return result;
    }

    private static Dictionary<string, string> ParseLocalization(JsonElement root)
    {
        JsonElement data = root;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out JsonElement nestedData)
            && nestedData.ValueKind == JsonValueKind.Object)
        {
            data = nestedData;
        }

        Dictionary<string, string> result = new(StringComparer.Ordinal);
        if (data.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in data.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String
                && property.Value.GetString() is string value)
            {
                result[property.Name] = value;
            }
        }

        return result;
    }

    private static IEnumerable<JsonElement> EnumerateData(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToArray();
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out JsonElement data)
            && data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray().ToArray();
        }

        return [];
    }

    private static bool IsShip(JsonElement unit)
    {
        if (!unit.TryGetProperty("combatType", out JsonElement combatType))
        {
            return false;
        }

        if (combatType.ValueKind == JsonValueKind.Number)
        {
            return combatType.TryGetInt32(out int value) && value == 2;
        }

        string? text = combatType.GetString();
        return string.Equals(text, "2", StringComparison.Ordinal)
            || text?.Contains("SHIP", StringComparison.OrdinalIgnoreCase) is true;
    }

    private static bool IsVisibleFactionCategory(
        string categoryId,
        IReadOnlyDictionary<string, CategoryDefinition> categories) =>
        IsFactionCategory(categoryId)
        && (!categories.TryGetValue(categoryId, out CategoryDefinition? category) || category.Visible);

    private static bool IsFactionCategory(string categoryId) =>
        categoryId.StartsWith("affiliation_", StringComparison.Ordinal)
        || categoryId.StartsWith("profession_", StringComparison.Ordinal)
        || categoryId.StartsWith("species_", StringComparison.Ordinal)
        || string.Equals(categoryId, "unaligned_force_user", StringComparison.Ordinal)
        || string.Equals(categoryId, "galactic_legend", StringComparison.Ordinal);

    private static string? ResolveText(IReadOnlyDictionary<string, string> localization, string? key) =>
        !string.IsNullOrWhiteSpace(key) && localization.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string FormatCategoryId(string categoryId)
    {
        int separator = categoryId.IndexOf('_', StringComparison.Ordinal);
        string value = separator >= 0 ? categoryId[(separator + 1)..] : categoryId;
        return value.Replace('_', ' ').Trim();
    }

    private static string[] GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
        ];
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out bool value) && value,
            _ => false
        };
    }

    private sealed record CategoryDefinition(string? DescriptionKey, bool Visible);
}
