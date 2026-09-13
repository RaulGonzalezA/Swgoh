using System.IO.Compression;
using System.Text.Json;

using Swgoh.Application.GameData;

namespace Swgoh.Infrastructure.GameData;

internal sealed class SwgohGameDataCatalogClient : ISwgohGameDataCatalog
{
    internal const string HttpClientName = "swgoh-game-data";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly IHttpClientFactory httpClientFactory;
    private readonly string locale;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private GameDataCatalog? cachedCatalog;
    private DateTimeOffset cacheExpiresAtUtc;

    public SwgohGameDataCatalogClient(IHttpClientFactory httpClientFactory, string locale = "ENG_US")
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        this.httpClientFactory = httpClientFactory;
        this.locale = NormalizeLocale(locale);
    }

    public async Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cachedCatalog is not null && DateTimeOffset.UtcNow < cacheExpiresAtUtc)
        {
            return cachedCatalog;
        }

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cachedCatalog is not null && DateTimeOffset.UtcNow < cacheExpiresAtUtc)
            {
                return cachedCatalog;
            }

            HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);
            Task<JsonDocument> unitsTask = GetJsonAsync(httpClient, "units_gas.json", cancellationToken);
            Task<JsonDocument> skillsTask = GetJsonAsync(httpClient, "skill.json", cancellationToken);
            Task<JsonDocument> guidesTask = GetJsonAsync(httpClient, "unitGuideDefinition.json", cancellationToken);
            Task<JsonDocument> requirementsTask = GetJsonAsync(httpClient, "requirement.json", cancellationToken);
            Task<JsonDocument> categoriesTask = GetJsonAsync(httpClient, "category.json", cancellationToken);
            Task<JsonDocument> localizationTask = GetBrotliJsonAsync(
                httpClient,
                $"Loc_{locale}.txt.json.br",
                cancellationToken);

            await Task.WhenAll(
                unitsTask,
                skillsTask,
                guidesTask,
                requirementsTask,
                categoriesTask,
                localizationTask).ConfigureAwait(false);

            using JsonDocument unitsDocument = await unitsTask.ConfigureAwait(false);
            using JsonDocument skillsDocument = await skillsTask.ConfigureAwait(false);
            using JsonDocument guidesDocument = await guidesTask.ConfigureAwait(false);
            using JsonDocument requirementsDocument = await requirementsTask.ConfigureAwait(false);
            using JsonDocument categoriesDocument = await categoriesTask.ConfigureAwait(false);
            using JsonDocument localizationDocument = await localizationTask.ConfigureAwait(false);

            IReadOnlyDictionary<string, string> localization = ParseLocalization(localizationDocument.RootElement);
            IReadOnlyDictionary<string, CategoryDefinition> categories = ParseCategories(categoriesDocument.RootElement);
            Dictionary<string, GameUnitDefinition> units = ParseUnits(
                unitsDocument.RootElement,
                categories,
                localization);
            Dictionary<string, GameSkillDefinition> skills = ParseSkills(skillsDocument.RootElement);
            IReadOnlyCollection<GalacticLegendDefinition> legends = ParseGalacticLegends(
                guidesDocument.RootElement,
                requirementsDocument.RootElement,
                units.Keys.ToHashSet(StringComparer.Ordinal));

            cachedCatalog = new GameDataCatalog(units, skills, legends);
            cacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
            return cachedCatalog;
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
        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in data.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is string value)
                {
                    result[property.Name] = value;
                }
            }
        }

        return result;
    }

    private static Dictionary<string, GameSkillDefinition> ParseSkills(JsonElement root)
    {
        Dictionary<string, GameSkillDefinition> result = new(StringComparer.Ordinal);
        foreach (JsonElement skill in EnumerateData(root))
        {
            string? id = GetString(skill, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result[id] = new GameSkillDefinition(
                id,
                FindSpecialTier(skill, "zeta"),
                FindSpecialTier(skill, "omicron"));
        }

        return result;
    }

    private static IReadOnlyCollection<GalacticLegendDefinition> ParseGalacticLegends(
        JsonElement guidesRoot,
        JsonElement requirementsRoot,
        IReadOnlySet<string> knownUnits)
    {
        Dictionary<string, JsonElement> requirements = EnumerateData(requirementsRoot)
            .Where(item => !string.IsNullOrWhiteSpace(GetString(item, "id")))
            .ToDictionary(item => GetString(item, "id")!, item => item, StringComparer.Ordinal);

        List<GalacticLegendDefinition> legends = [];
        foreach (JsonElement guide in EnumerateData(guidesRoot))
        {
            if (!GetBoolean(guide, "galacticLegend"))
            {
                continue;
            }

            string? unitBaseId = GetString(guide, "unitBaseId");
            string? requirementId = GetString(guide, "additionalActivationRequirementId");
            if (string.IsNullOrWhiteSpace(unitBaseId)
                || string.Equals(unitBaseId, "TBA", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(requirementId))
            {
                continue;
            }

            IReadOnlyCollection<GalacticLegendUnitRequirement> unitRequirements = requirements.TryGetValue(requirementId, out JsonElement requirement)
                ? ExtractUnitRequirements(requirement, knownUnits)
                : [];

            legends.Add(new GalacticLegendDefinition(unitBaseId, requirementId, unitRequirements));
        }

        return legends
            .GroupBy(legend => legend.UnitBaseId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyCollection<GalacticLegendUnitRequirement> ExtractUnitRequirements(
        JsonElement root,
        IReadOnlySet<string> knownUnits)
    {
        Dictionary<string, RequirementAccumulator> requirements = new(StringComparer.Ordinal);
        VisitRequirement(root, knownUnits, requirements);
        return
        [
            .. requirements
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new GalacticLegendUnitRequirement(
                    pair.Key,
                    pair.Value.MinimumRarity,
                    pair.Value.MinimumGearTier,
                    pair.Value.MinimumRelicTier))
        ];
    }

    private static void VisitRequirement(
        JsonElement element,
        IReadOnlySet<string> knownUnits,
        IDictionary<string, RequirementAccumulator> requirements)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? unitBaseId = FindKnownUnitId(element, knownUnits);
            if (unitBaseId is not null)
            {
                requirements.TryGetValue(unitBaseId, out RequirementAccumulator? existing);
                RequirementAccumulator current = existing ?? new RequirementAccumulator();
                requirements[unitBaseId] = current with
                {
                    MinimumRarity = Math.Max(current.MinimumRarity, FindNumericMinimum(element, "rarity")),
                    MinimumGearTier = Math.Max(current.MinimumGearTier, FindNumericMinimum(element, "gear")),
                    MinimumRelicTier = Math.Max(current.MinimumRelicTier, FindNumericMinimum(element, "relic"))
                };
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                VisitRequirement(property.Value, knownUnits, requirements);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                VisitRequirement(item, knownUnits, requirements);
            }
        }
    }

    private static string? FindKnownUnitId(JsonElement element, IReadOnlySet<string> knownUnits)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? candidate = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            int separator = candidate.IndexOf(':', StringComparison.Ordinal);
            string normalized = separator >= 0 ? candidate[..separator] : candidate;
            if (knownUnits.Contains(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static int FindNumericMinimum(JsonElement element, string marker)
    {
        int result = 0;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result = Math.Max(result, GetInt32(property.Value));
        }

        return result;
    }

    private static int? FindSpecialTier(JsonElement skill, string marker)
    {
        foreach (JsonProperty property in skill.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array
                || !property.Name.Contains("tier", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int index = 0;
            foreach (JsonElement tier in property.Value.EnumerateArray())
            {
                if (ContainsTrueMarker(tier, marker))
                {
                    return TryGetInt32(tier, "tier") ?? index + 2;
                }

                index++;
            }
        }

        return ContainsTrueMarker(skill, marker) ? TryGetInt32(skill, "tier") : null;
    }

    private static bool ContainsTrueMarker(JsonElement element, string marker)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind is JsonValueKind.True)
                {
                    return true;
                }

                if (ContainsTrueMarker(property.Value, marker))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (ContainsTrueMarker(item, marker))
                {
                    return true;
                }
            }
        }

        return false;
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

    private static string NormalizeLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        string normalized = locale.Trim().ToUpperInvariant();
        if (normalized.Length > 16
            || normalized.Any(character => !(char.IsAsciiLetter(character) || character == '_')))
        {
            throw new ArgumentException("Game Data locale must contain only ASCII letters and underscores.", nameof(locale));
        }

        return normalized;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property)
        && property.ValueKind == JsonValueKind.True;

    private static int? TryGetInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property)
            ? GetInt32(property)
            : null;

    private static int GetInt32(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value))
        {
            return value;
        }

        return element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value)
            ? value
            : 0;
    }

    private sealed record CategoryDefinition(string? DescriptionKey, bool Visible);

    private sealed record RequirementAccumulator(
        int MinimumRarity = 0,
        int MinimumGearTier = 0,
        int MinimumRelicTier = 0);
}
