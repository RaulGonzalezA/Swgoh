using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class SwgohComlinkClient(
    HttpClient httpClient,
    ISwgohStatsClient statsClient,
    ISwgohGameDataCatalog gameDataCatalog) : ISwgohPlayerClient
{
    private const int GameDataSkillTierOffset = 2;
    private const int SpeedStatId = 5;
    private const decimal RawStatScale = 100_000_000m;

    public async Task<ImportedPlayer> GetPlayerAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(allyCode, 100_000_000L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(allyCode, 999_999_999L);

        ComlinkPlayerRequest request = new(new ComlinkPlayerPayload(allyCode.ToString(CultureInfo.InvariantCulture)), false);
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("player", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ComlinkPlayerDto player;
        JsonDocument rawPlayer;
        try
        {
            player = JsonSerializer.Deserialize<ComlinkPlayerDto>(json)
                ?? throw InvalidProviderData("Comlink returned an empty player payload.");
            rawPlayer = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw InvalidProviderData("Comlink returned invalid player JSON.", exception);
        }

        using (rawPlayer)
        {
            ValidatePlayer(player, allyCode);

            if (!rawPlayer.RootElement.TryGetProperty("rosterUnit", out JsonElement rosterElement)
                || rosterElement.ValueKind != JsonValueKind.Array)
            {
                throw InvalidProviderData("Comlink player payload does not contain a valid rosterUnit array.");
            }

            JsonElement[] rawRoster = [.. rosterElement.EnumerateArray().Select(unit => unit.Clone())];
            if (rawRoster.Length != player.RosterUnit.Count)
            {
                throw InvalidProviderData("Comlink roster payload could not be mapped completely.");
            }

            ValidateRosterIdentity(player.RosterUnit);

            Task<IReadOnlyDictionary<string, CalculatedRosterUnitStats>> statsTask = statsClient
                .CalculateRosterStatsAsync(rawRoster, cancellationToken);
            Task<GameDataCatalog> catalogTask = gameDataCatalog.GetAsync(cancellationToken);
            await Task.WhenAll(statsTask, catalogTask).ConfigureAwait(false);

            IReadOnlyDictionary<string, CalculatedRosterUnitStats> calculatedByUnit = await statsTask.ConfigureAwait(false);
            GameDataCatalog catalog = await catalogTask.ConfigureAwait(false);
            ImportedRosterUnit[] roster =
            [
                .. player.RosterUnit.Select(unit => MapRosterUnit(unit, calculatedByUnit, catalog))
            ];
            long galacticPower = roster.Sum(unit => unit.GalacticPower);
            PlayerDatacron[] datacrons = ParseDatacrons(rawPlayer.RootElement);

            return new ImportedPlayer(
                allyCode,
                player.PlayerId!.Trim(),
                player.Name!.Trim(),
                player.GuildId,
                player.GuildName,
                player.Level,
                galacticPower,
                roster,
                datacrons);
        }
    }

    private static ImportedRosterUnit MapRosterUnit(
        ComlinkRosterUnitDto unit,
        IReadOnlyDictionary<string, CalculatedRosterUnitStats> calculatedByUnit,
        GameDataCatalog catalog)
    {
        string id = unit.Id!.Trim();
        string definitionId = NormalizeDefinitionId(unit.DefinitionId);

        if (!calculatedByUnit.TryGetValue(id, out CalculatedRosterUnitStats? calculated))
        {
            throw InvalidProviderData($"SWGOH Stats did not return calculated data for roster unit '{id}'.");
        }

        if (calculated.GalacticPower < 0)
        {
            throw InvalidProviderData($"SWGOH Stats returned a negative Galactic Power for roster unit '{id}'.");
        }

        if (!catalog.Units.TryGetValue(definitionId, out GameUnitDefinition? definition))
        {
            throw InvalidProviderData($"Game Data does not contain roster unit definition '{definitionId}'.");
        }

        int zetaCount = 0;
        int omicronCount = 0;
        foreach (ComlinkSkillDto playerSkill in unit.Skill)
        {
            if (!catalog.Skills.TryGetValue(playerSkill.Id, out GameSkillDefinition? skill))
            {
                continue;
            }

            int currentSkillTier = ToGameDataSkillTier(playerSkill.Tier);
            if (skill.ZetaTier is int zetaTier && currentSkillTier >= zetaTier)
            {
                zetaCount++;
            }

            if (skill.OmicronTier is int omicronTier && currentSkillTier >= omicronTier)
            {
                omicronCount++;
            }
        }

        RosterModSummary? modSummary = definition.IsShip
            ? null
            : BuildModSummary(unit.EquippedStatMod ?? []);
        return new ImportedRosterUnit(
            id,
            definitionId,
            unit.CurrentLevel,
            unit.CurrentRarity,
            unit.CurrentTier,
            NormalizeRelicTier(unit.Relic?.CurrentTier ?? 0),
            unit.EquippedStatMod?.Count ?? 0,
            calculated.GalacticPower,
            definition.IsShip,
            zetaCount,
            omicronCount,
            calculated.Stats,
            modSummary);
    }

    private static RosterModSummary BuildModSummary(IReadOnlyCollection<JsonElement> mods)
    {
        int sixDotCount = 0;
        int speedSetModCount = 0;
        int speedPrimaryCount = 0;
        decimal speedBonus = 0m;
        bool hasSpeedValue = false;

        foreach (JsonElement mod in mods)
        {
            string definitionId = ReadScalarString(mod, "definitionId") ?? string.Empty;
            if (definitionId.Length >= 2 && definitionId[1] == '6')
            {
                sixDotCount++;
            }

            if (definitionId.Length >= 1 && definitionId[0] == '4')
            {
                speedSetModCount++;
            }

            if (TryGetStat(mod, "primaryStat", out int primaryId, out decimal primaryValue))
            {
                if (primaryId == SpeedStatId)
                {
                    speedPrimaryCount++;
                    speedBonus += primaryValue;
                    hasSpeedValue = true;
                }
            }

            if (mod.TryGetProperty("secondaryStat", out JsonElement secondaryStats)
                && secondaryStats.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement secondary in secondaryStats.EnumerateArray())
                {
                    if (TryReadStat(secondary, out int statId, out decimal value) && statId == SpeedStatId)
                    {
                        speedBonus += value;
                        hasSpeedValue = true;
                    }
                }
            }
        }

        return new RosterModSummary(
            mods.Count,
            sixDotCount,
            speedSetModCount,
            speedPrimaryCount,
            hasSpeedValue ? Math.Round(speedBonus, 2) : null);
    }

    private static bool TryGetStat(JsonElement source, string propertyName, out int statId, out decimal value)
    {
        statId = default;
        value = default;
        return source.TryGetProperty(propertyName, out JsonElement stat)
            && stat.ValueKind == JsonValueKind.Object
            && TryReadStat(stat, out statId, out value);
    }

    private static bool TryReadStat(JsonElement stat, out int statId, out decimal value)
    {
        statId = ReadNullableInt(stat, "unitStatId") ?? ReadNullableInt(stat, "unitStat") ?? 0;
        JsonElement rawValue;
        if (!stat.TryGetProperty("unscaledDecimalValue", out rawValue)
            && !stat.TryGetProperty("value", out rawValue))
        {
            value = default;
            return false;
        }

        decimal? parsed = ReadDecimal(rawValue);
        if (statId <= 0 || parsed is null)
        {
            value = default;
            return false;
        }

        value = parsed.Value / RawStatScale;
        return true;
    }

    private static PlayerDatacron[] ParseDatacrons(JsonElement player)
    {
        if (!player.TryGetProperty("datacron", out JsonElement datacrons)
            || datacrons.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<PlayerDatacron>();
        foreach (JsonElement datacron in datacrons.EnumerateArray())
        {
            if (datacron.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? id = ReadScalarString(datacron, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            PlayerDatacronAffix[] affixes = ParseDatacronAffixes(datacron);
            result.Add(new PlayerDatacron(
                id.Trim(),
                ReadScalarString(datacron, "setId") ?? string.Empty,
                ReadScalarString(datacron, "templateId") ?? string.Empty,
                ReadNullableInt(datacron, "tier") ?? affixes.Length,
                ReadBoolean(datacron, "locked"),
                affixes));
        }

        return [.. result];
    }

    private static PlayerDatacronAffix[] ParseDatacronAffixes(JsonElement datacron)
    {
        if (!datacron.TryGetProperty("affix", out JsonElement affixes)
            || affixes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. affixes.EnumerateArray()
                .Where(affix => affix.ValueKind == JsonValueKind.Object)
                .Select(affix => new PlayerDatacronAffix(
                    ReadScalarString(affix, "abilityId"),
                    ReadNullableInt(affix, "statType"),
                    ReadNullableLong(affix, "statValue") ?? ReadNullableLong(affix, "value"),
                    ReadNullableInt(affix, "requiredRelicTier"),
                    ReadStringArray(affix, "tag")))
        ];
    }

    private static IReadOnlyCollection<string> ReadStringArray(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                .Select(value => value.GetString()!.Trim())
        ];
    }

    private static string? ReadScalarString(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? ReadNullableInt(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : null;
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

    private static decimal? ReadDecimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)
                ? parsed
                : null;
    }

    private static bool ReadBoolean(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private static void ValidatePlayer(ComlinkPlayerDto player, long requestedAllyCode)
    {
        if (!long.TryParse(player.AllyCode, NumberStyles.None, CultureInfo.InvariantCulture, out long returnedAllyCode))
        {
            throw InvalidProviderData("Comlink returned an invalid ally code.");
        }

        if (returnedAllyCode != requestedAllyCode)
        {
            throw InvalidProviderData(
                $"Comlink returned ally code '{returnedAllyCode}' for requested ally code '{requestedAllyCode}'.");
        }

        if (string.IsNullOrWhiteSpace(player.PlayerId))
        {
            throw InvalidProviderData("Comlink returned a player without playerId.");
        }

        if (string.IsNullOrWhiteSpace(player.Name))
        {
            throw InvalidProviderData("Comlink returned a player without a name.");
        }

        if (player.RosterUnit is null)
        {
            throw InvalidProviderData("Comlink returned a player without rosterUnit data.");
        }
    }

    private static void ValidateRosterIdentity(IReadOnlyCollection<ComlinkRosterUnitDto> roster)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (ComlinkRosterUnitDto unit in roster)
        {
            if (string.IsNullOrWhiteSpace(unit.Id))
            {
                throw InvalidProviderData("Comlink returned a roster unit without an id.");
            }

            string id = unit.Id.Trim();
            if (!ids.Add(id))
            {
                throw InvalidProviderData($"Comlink returned duplicate roster unit id '{id}'.");
            }

            if (string.IsNullOrWhiteSpace(NormalizeDefinitionId(unit.DefinitionId)))
            {
                throw InvalidProviderData($"Comlink returned roster unit '{id}' without a definitionId.");
            }
        }
    }

    private static int ToGameDataSkillTier(int comlinkPlayerSkillTier) =>
        checked(comlinkPlayerSkillTier + GameDataSkillTierOffset);

    private static string NormalizeDefinitionId(string? definitionId)
    {
        string value = definitionId?.Trim() ?? string.Empty;
        int separatorIndex = value.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex >= 0 ? value[..separatorIndex] : value;
    }

    private static int NormalizeRelicTier(int currentTier) => currentTier <= 2 ? 0 : currentTier - 2;

    private static HttpRequestException InvalidProviderData(string message, Exception? innerException = null) =>
        new(message, innerException);

    private sealed record ComlinkPlayerRequest(
        [property: JsonPropertyName("payload")] ComlinkPlayerPayload Payload,
        [property: JsonPropertyName("enums")] bool Enums);

    private sealed record ComlinkPlayerPayload(
        [property: JsonPropertyName("allyCode")] string AllyCode);

    private sealed class ComlinkPlayerDto
    {
        [JsonPropertyName("allyCode")]
        public string AllyCode { get; init; } = string.Empty;

        [JsonPropertyName("playerId")]
        public string? PlayerId { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("guildId")]
        public string? GuildId { get; init; }

        [JsonPropertyName("guildName")]
        public string? GuildName { get; init; }

        [JsonPropertyName("level")]
        public int Level { get; init; }

        [JsonPropertyName("rosterUnit")]
        public List<ComlinkRosterUnitDto> RosterUnit { get; init; } = [];
    }

    private sealed class ComlinkRosterUnitDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("definitionId")]
        public string? DefinitionId { get; init; }

        [JsonPropertyName("currentLevel")]
        public int CurrentLevel { get; init; }

        [JsonPropertyName("currentRarity")]
        public int CurrentRarity { get; init; }

        [JsonPropertyName("currentTier")]
        public int CurrentTier { get; init; }

        [JsonPropertyName("relic")]
        public ComlinkRelicDto? Relic { get; init; }

        [JsonPropertyName("equippedStatMod")]
        public List<JsonElement>? EquippedStatMod { get; init; }

        [JsonPropertyName("skill")]
        public List<ComlinkSkillDto> Skill { get; init; } = [];
    }

    private sealed class ComlinkSkillDto
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("tier")]
        public int Tier { get; init; }
    }

    private sealed class ComlinkRelicDto
    {
        [JsonPropertyName("currentTier")]
        public int CurrentTier { get; init; }
    }
}
