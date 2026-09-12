using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Swgoh.Application.Players;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class SwgohComlinkClient(HttpClient httpClient) : ISwgohPlayerClient
{
    public async Task<ImportedPlayer> GetPlayerAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

        ComlinkPlayerRequest request = new(new ComlinkPlayerPayload(allyCode.ToString()), false);
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("player", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        ComlinkPlayerDto? player = await response.Content.ReadFromJsonAsync<ComlinkPlayerDto>(cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            throw new InvalidOperationException("Comlink returned an empty player payload.");
        }

        long returnedAllyCode = long.TryParse(player.AllyCode, out long parsedAllyCode) ? parsedAllyCode : allyCode;
        ImportedRosterUnit[] roster = [.. player.RosterUnit.Select(MapRosterUnit)];

        return new ImportedPlayer(
            returnedAllyCode,
            player.PlayerId ?? string.Empty,
            player.Name ?? $"Player {returnedAllyCode}",
            player.GuildId,
            player.GuildName,
            player.Level,
            0,
            roster);
    }

    private static ImportedRosterUnit MapRosterUnit(ComlinkRosterUnitDto unit)
    {
        string definitionId = unit.DefinitionId ?? string.Empty;
        int separatorIndex = definitionId.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            definitionId = definitionId[..separatorIndex];
        }

        return new ImportedRosterUnit(
            unit.Id ?? string.Empty,
            definitionId,
            unit.CurrentLevel,
            unit.CurrentRarity,
            unit.CurrentTier,
            unit.Relic?.CurrentTier ?? 0,
            unit.EquippedStatMod?.Count ?? 0);
    }

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
    }

    private sealed class ComlinkRelicDto
    {
        [JsonPropertyName("currentTier")]
        public int CurrentTier { get; init; }
    }
}
