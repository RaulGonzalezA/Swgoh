using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Swgoh.Application.GameData;
using Swgoh.Application.Players;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class SwgohComlinkClient(
    HttpClient httpClient,
    ISwgohStatsClient statsClient,
    ISwgohGameDataCatalog gameDataCatalog) : ISwgohPlayerClient
{
    private const int GameDataSkillTierOffset = 2;

    public async Task<ImportedPlayer> GetPlayerAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

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

            Task<IReadOnlyDictionary<string, long>> powerTask = statsClient.CalculateGalacticPowerAsync(rawRoster, cancellationToken);
            Task<GameDataCatalog> catalogTask = gameDataCatalog.GetAsync(cancellationToken);
            await Task.WhenAll(powerTask, catalogTask).ConfigureAwait(false);

            IReadOnlyDictionary<string, long> powerByUnit = await powerTask.ConfigureAwait(false);
            GameDataCatalog catalog = await catalogTask.ConfigureAwait(false);
            ImportedRosterUnit[] roster = [.. player.RosterUnit.Select(unit => MapRosterUnit(unit, powerByUnit, catalog))];
            long galacticPower = roster.Sum(unit => unit.GalacticPower);

            return new ImportedPlayer(
                allyCode,
                player.PlayerId!.Trim(),
                player.Name!.Trim(),
                player.GuildId,
                player.GuildName,
                player.Level,
                galacticPower,
                roster);
        }
    }

    private static ImportedRosterUnit MapRosterUnit(
        ComlinkRosterUnitDto unit,
        IReadOnlyDictionary<string, long> powerByUnit,
        GameDataCatalog catalog)
    {
        string id = unit.Id!.Trim();
        string definitionId = NormalizeDefinitionId(unit.DefinitionId);

        if (!powerByUnit.TryGetValue(id, out long galacticPower))
        {
            throw InvalidProviderData($"SWGOH Stats did not return Galactic Power for roster unit '{id}'.");
        }

        if (galacticPower < 0)
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

        return new ImportedRosterUnit(
            id,
            definitionId,
            unit.CurrentLevel,
            unit.CurrentRarity,
            unit.CurrentTier,
            NormalizeRelicTier(unit.Relic?.CurrentTier ?? 0),
            unit.EquippedStatMod?.Count ?? 0,
            galacticPower,
            definition.IsShip,
            zetaCount,
            omicronCount);
    }

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
        public List<object>? EquippedStatMod { get; init; }

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
