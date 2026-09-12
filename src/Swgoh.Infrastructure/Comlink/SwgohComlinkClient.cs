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
    private const int PlayerSkillTierOffset = 2;

    public async Task<ImportedPlayer> GetPlayerAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

        ComlinkPlayerRequest request = new(new ComlinkPlayerPayload(allyCode.ToString()), false);
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("player", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ComlinkPlayerDto? player = JsonSerializer.Deserialize<ComlinkPlayerDto>(json);
        if (player is null)
        {
            throw new InvalidOperationException("Comlink returned an empty player payload.");
        }

        using JsonDocument rawPlayer = JsonDocument.Parse(json);
        JsonElement[] rawRoster = rawPlayer.RootElement.TryGetProperty("rosterUnit", out JsonElement rosterElement)
            ? [.. rosterElement.EnumerateArray().Select(unit => unit.Clone())]
            : [];

        Task<IReadOnlyDictionary<string, long>> powerTask = statsClient.CalculateGalacticPowerAsync(rawRoster, cancellationToken);
        Task<GameDataCatalog> catalogTask = gameDataCatalog.GetAsync(cancellationToken);
        await Task.WhenAll(powerTask, catalogTask).ConfigureAwait(false);

        IReadOnlyDictionary<string, long> powerByUnit = await powerTask.ConfigureAwait(false);
        GameDataCatalog catalog = await catalogTask.ConfigureAwait(false);
        long returnedAllyCode = long.TryParse(player.AllyCode, out long parsedAllyCode) ? parsedAllyCode : allyCode;
        ImportedRosterUnit[] roster = [.. player.RosterUnit.Select(unit => MapRosterUnit(unit, powerByUnit, catalog))];
        long galacticPower = roster.Sum(unit => unit.GalacticPower);

        return new ImportedPlayer(
            returnedAllyCode,
            player.PlayerId ?? string.Empty,
            player.Name ?? $"Player {returnedAllyCode}",
            player.GuildId,
            player.GuildName,
            player.Level,
            galacticPower,
            roster);
    }

    private static ImportedRosterUnit MapRosterUnit(
        ComlinkRosterUnitDto unit,
        IReadOnlyDictionary<string, long> powerByUnit,
        GameDataCatalog catalog)
    {
        string definitionId = NormalizeDefinitionId(unit.DefinitionId);
        string id = unit.Id ?? string.Empty;
        long galacticPower = powerByUnit.TryGetValue(id, out long gp) ? gp : 0;
        bool isShip = catalog.Units.TryGetValue(definitionId, out GameUnitDefinition? definition) && definition.IsShip;

        int zetaCount = 0;
        int omicronCount = 0;
        foreach (ComlinkSkillDto playerSkill in unit.Skill)
        {
            if (!catalog.Skills.TryGetValue(playerSkill.Id, out GameSkillDefinition? skill))
            {
                continue;
            }

            int currentSkillTier = playerSkill.Tier + PlayerSkillTierOffset;
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
            isShip,
            zetaCount,
            omicronCount);
    }

    private static string NormalizeDefinitionId(string? definitionId)
    {
        string value = definitionId ?? string.Empty;
        int separatorIndex = value.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex >= 0 ? value[..separatorIndex] : value;
    }

    private static int NormalizeRelicTier(int currentTier) => currentTier <= 2 ? 0 : currentTier - 2;

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
