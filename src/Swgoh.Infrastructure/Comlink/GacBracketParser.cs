using System.Text.Json;

using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed record GacBracketParticipant(
    long? AllyCode,
    string Name,
    string? PlayerId,
    JsonElement Element);

internal sealed record GacActiveEvent(
    string EventId,
    string EventInstanceId,
    long StartTime,
    long EndTime,
    JsonElement EventElement,
    JsonElement InstanceElement);

internal static class GacBracketParser
{
    public static IReadOnlyList<GacBracketParticipant> ReadParticipants(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(body);
        if (!TryGetLeaderboardPlayers(document.RootElement, out JsonElement players))
        {
            return [];
        }

        return
        [
            .. players.EnumerateArray()
                .Select(ReadParticipant)
                .Where(participant => participant is not null)
                .Select(participant => participant!)
        ];
    }

    public static GacActiveEvent? ReadActiveEvent(JsonElement root)
    {
        if (!TryGetProperty(root, "gameEvent", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        GacActiveEvent[] contexts =
        [
            .. events.EnumerateArray()
                .Where(IsGacEvent)
                .SelectMany(ReadEventInstances)
        ];

        return contexts
            .Where(context => context.StartTime <= now && now <= context.EndTime)
            .OrderByDescending(context => context.StartTime)
            .FirstOrDefault()
            ?? contexts.OrderByDescending(context => context.StartTime).FirstOrDefault();
    }

    public static string? ReadPlayerId(JsonElement root) =>
        ReadString(root, "playerId") ?? ReadString(root, "id");

    public static string ReadPlayerName(JsonElement root, string fallback) =>
        ReadString(root, "name") ?? fallback;

    public static long? ReadAllyCode(JsonElement root) =>
        TryReadLongProperty(root, "allyCode", out long allyCode) ? allyCode : null;

    public static int? ReadSkillRating(JsonElement root)
    {
        if (TryGetProperty(root, "playerRating", out JsonElement playerRating))
        {
            if (TryGetProperty(playerRating, "playerSkillRating", out JsonElement playerSkillRating) &&
                TryGetProperty(playerSkillRating, "skillRating", out JsonElement nestedSkillRating) &&
                TryReadInt(nestedSkillRating, out int nestedValue))
            {
                return nestedValue;
            }

            if (TryGetProperty(playerRating, "skillRating", out JsonElement directSkillRating) &&
                TryReadInt(directSkillRating, out int directValue))
            {
                return directValue;
            }
        }

        return null;
    }

    public static int? ReadRoundNumber(JsonElement element)
    {
        foreach (string propertyName in new[] { "round", "roundNumber", "currentRound" })
        {
            if (TryGetProperty(element, propertyName, out JsonElement property) &&
                TryReadInt(property, out int round) && round is >= 1 and <= 3)
            {
                return round;
            }
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    int? nested = ReadRoundNumber(property.Value);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }
            }
        }

        return null;
    }

    public static bool TryReadPvpInt(JsonElement participant, string name, out int value)
    {
        value = default;
        return TryGetProperty(participant, "pvpStatus", out JsonElement pvpStatus) &&
               TryGetProperty(pvpStatus, name, out JsonElement property) &&
               TryReadInt(property, out value);
    }

    public static int FindPlayerIndex(
        IReadOnlyList<GacBracketParticipant> participants,
        string playerId,
        long allyCode)
    {
        for (int index = 0; index < participants.Count; index++)
        {
            GacBracketParticipant participant = participants[index];
            if (string.Equals(participant.PlayerId, playerId, StringComparison.Ordinal) ||
                participant.AllyCode == allyCode)
            {
                return index;
            }
        }

        return -1;
    }

    public static bool IsValidAllyCode(long allyCode) => allyCode is >= 100_000_000 and <= 999_999_999;

    private static GacBracketParticipant? ReadParticipant(JsonElement element)
    {
        string? playerId = ReadString(element, "id") ?? ReadString(element, "playerId");
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        long? allyCode = TryReadLongProperty(element, "allyCode", out long value) ? value : null;
        string name = ReadString(element, "name") ?? ReadString(element, "playerName") ?? playerId;
        return new GacBracketParticipant(allyCode, name, playerId, element.Clone());
    }

    private static bool TryGetLeaderboardPlayers(JsonElement root, out JsonElement players)
    {
        if (TryGetProperty(root, "player", out players) &&
            players.ValueKind == JsonValueKind.Array &&
            players.GetArrayLength() > 0)
        {
            return true;
        }

        if (TryGetProperty(root, "leaderboard", out JsonElement leaderboard))
        {
            if (leaderboard.ValueKind == JsonValueKind.Object &&
                TryGetProperty(leaderboard, "player", out players) &&
                players.ValueKind == JsonValueKind.Array &&
                players.GetArrayLength() > 0)
            {
                return true;
            }

            if (leaderboard.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in leaderboard.EnumerateArray())
                {
                    if (TryGetProperty(entry, "player", out players) &&
                        players.ValueKind == JsonValueKind.Array &&
                        players.GetArrayLength() > 0)
                    {
                        return true;
                    }
                }
            }
        }

        players = default;
        return false;
    }

    private static IEnumerable<GacActiveEvent> ReadEventInstances(JsonElement eventElement)
    {
        string? eventId = ReadString(eventElement, "id");
        if (string.IsNullOrWhiteSpace(eventId) ||
            !TryGetProperty(eventElement, "instance", out JsonElement instances) ||
            instances.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement instance in instances.EnumerateArray())
        {
            string? instanceId = ReadString(instance, "id");
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                continue;
            }

            long startTime = ReadLong(instance, "startTime") ?? long.MinValue;
            long endTime = ReadLong(instance, "endTime") ?? long.MaxValue;
            yield return new GacActiveEvent(
                eventId,
                $"{eventId}:{instanceId}",
                startTime,
                endTime,
                eventElement.Clone(),
                instance.Clone());
        }
    }

    private static bool IsGacEvent(JsonElement element) =>
        TryGetProperty(element, "type", out JsonElement type) &&
        TryReadInt(type, out int value) && value == 10;

    private static long? ReadLong(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out JsonElement property) &&
        long.TryParse(JsonString(property), out long value)
            ? value
            : null;

    private static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) ? JsonString(value) : null;

    private static bool TryReadLongProperty(JsonElement element, string name, out long value)
    {
        value = default;
        return TryGetProperty(element, name, out JsonElement property) &&
               long.TryParse(JsonString(property), out value);
    }

    private static bool TryReadInt(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }

        return int.TryParse(JsonString(element), out value);
    }

    private static string? JsonString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
