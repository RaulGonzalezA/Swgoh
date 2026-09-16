using System.Globalization;
using System.Text.Json;

using Swgoh.Application.TerritoryBattles;
using Swgoh.Infrastructure.GameData;

namespace Swgoh.Infrastructure.TerritoryBattles;

internal sealed class SwgohRiseOfEmpireOperationsCatalogClient(IHttpClientFactory httpClientFactory)
    : IRiseOfEmpireOperationsCatalog
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private IReadOnlyCollection<RiseOfEmpireOperationDefinition>? cached;
    private DateTimeOffset cacheExpiresAtUtc;

    public async Task<IReadOnlyCollection<RiseOfEmpireOperationDefinition>> GetAsync(
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
            using Stream stream = await client.GetStreamAsync("swgoh_rote_operations.json", cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            cached = Parse(document.RootElement);
            cacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
            return cached;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    internal static IReadOnlyCollection<RiseOfEmpireOperationDefinition> Parse(JsonElement root)
    {
        JsonElement data = root;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Array)
        {
            data = nested;
        }

        if (data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("RotE operations Game Data must be an array.");
        }

        var operations = new List<RiseOfEmpireOperationDefinition>();
        foreach (JsonElement operation in data.EnumerateArray())
        {
            string? id = ReadString(operation, "id");
            int? phase = ParsePhase(ReadString(operation, "phase"));
            if (string.IsNullOrWhiteSpace(id) || phase is null)
            {
                continue;
            }

            RiseOfEmpireOperationSquadDefinition[] squads = ParseSquads(operation);
            operations.Add(new RiseOfEmpireOperationDefinition(
                id,
                phase.Value,
                NormalizePlanetName(ReadString(operation, "nameKey") ?? id),
                ReadString(operation, "type") ?? string.Empty,
                ReadBool(operation, "bonus"),
                ReadLong(operation, "totalPoints") ?? squads.Sum(squad => squad.Points),
                squads));
        }

        return operations
            .OrderBy(operation => operation.Phase)
            .ThenBy(operation => operation.PlanetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static RiseOfEmpireOperationSquadDefinition[] ParseSquads(JsonElement operation)
    {
        if (!operation.TryGetProperty("squads", out JsonElement squads) || squads.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. squads.EnumerateArray().Select((squad, index) => new RiseOfEmpireOperationSquadDefinition(
                ReadString(squad, "id") ?? $"squad-{index + 1}",
                ReadLong(squad, "points") ?? 0,
                ParseUnits(squad)))
        ];
    }

    private static RiseOfEmpireOperationUnitDefinition[] ParseUnits(JsonElement squad)
    {
        if (!squad.TryGetProperty("units", out JsonElement units) || units.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. units.EnumerateArray()
                .Select(ParseUnit)
                .Where(unit => unit is not null)
                .Select(unit => unit!)
        ];
    }

    private static RiseOfEmpireOperationUnitDefinition? ParseUnit(JsonElement unit)
    {
        string? baseId = ReadString(unit, "baseId") ?? ReadString(unit, "unitIdentifier");
        if (string.IsNullOrWhiteSpace(baseId))
        {
            return null;
        }

        int combatType = ReadInt(unit, "combatType") ?? 1;
        bool isShip = combatType == 2;
        int rawRelicTier = ReadInt(unit, "unitRelicTier") ?? 0;
        return new RiseOfEmpireOperationUnitDefinition(
            baseId.Trim(),
            ReadString(unit, "nameKey") ?? baseId.Trim(),
            isShip,
            ReadInt(unit, "rarity") ?? 7,
            isShip ? 0 : Math.Max(0, rawRelicTier - 2));
    }

    private static int? ParsePhase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string numeric = value.Trim().TrimStart('P', 'p');
        return int.TryParse(numeric, NumberStyles.None, CultureInfo.InvariantCulture, out int phase)
            ? phase
            : null;
    }

    private static string NormalizePlanetName(string value)
    {
        const string suffix = " Operation";
        string trimmed = value.Trim();
        return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^suffix.Length]
            : trimmed;
    }

    private static string? ReadString(JsonElement source, string propertyName)
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

    private static int? ReadInt(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;

    private static long? ReadLong(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : null;

    private static bool ReadBool(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
