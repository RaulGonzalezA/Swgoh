using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class PersistedGacOpponentSource(
    BackgroundGacOpponentSource fallback,
    IGacBracketLocationRepository locations,
    IHttpClientFactory httpClientFactory,
    ILogger<PersistedGacOpponentSource> logger) : ICurrentGacOpponentSource
{
    private static readonly TimeSpan ResultCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PersistedAttemptDuration = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, ResultCacheEntry> resultCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> persistedAttempts = new(StringComparer.Ordinal);

    public async Task<CurrentGacOpponentLookup> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken = default)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

        string key = $"{allyCode}:{formatOverride?.ToString() ?? "auto"}";
        if (resultCache.TryGetValue(key, out ResultCacheEntry? cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            GacTelemetry.RecordOpponentLookup(TimeSpan.Zero, cached.Lookup.Status, cacheHit: true, "persisted-wrapper-memory");
            return cached.Lookup;
        }

        PlayerContext? playerContext = null;
        if (!persistedAttempts.TryGetValue(key, out DateTimeOffset attemptedUntil) || attemptedUntil <= DateTimeOffset.UtcNow)
        {
            persistedAttempts[key] = DateTimeOffset.UtcNow.Add(PersistedAttemptDuration);
            try
            {
                playerContext = await ReadPlayerContextAsync(allyCode, cancellationToken).ConfigureAwait(false);
                CurrentGacOpponentLookup? persisted = await TryResolvePersistedAsync(
                    allyCode,
                    formatOverride,
                    playerContext,
                    cancellationToken).ConfigureAwait(false);
                if (persisted is not null)
                {
                    resultCache[key] = new ResultCacheEntry(persisted, DateTimeOffset.UtcNow.Add(ResultCacheDuration));
                    logger.LogInformation(
                        "Resolved GAC opponent for {AllyCode} from persisted bracket {BracketId}",
                        allyCode,
                        persisted.Opponent?.BracketId);
                    return persisted;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Persisted GAC bracket lookup failed for {AllyCode}; falling back to live search", allyCode);
            }
        }

        CurrentGacOpponentLookup lookup = await fallback
            .GetAsync(allyCode, formatOverride, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Status != CurrentGacOpponentStatus.Found || lookup.Opponent is null)
        {
            return lookup;
        }

        resultCache[key] = new ResultCacheEntry(lookup, DateTimeOffset.UtcNow.Add(ResultCacheDuration));
        try
        {
            playerContext ??= await ReadPlayerContextAsync(allyCode, cancellationToken).ConfigureAwait(false);
            await PersistLocationAsync(lookup.Opponent, playerContext?.SkillRating ?? 0, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not persist GAC bracket location for {AllyCode}", allyCode);
        }

        return lookup;
    }

    private async Task<CurrentGacOpponentLookup?> TryResolvePersistedAsync(
        long allyCode,
        GacFormat? formatOverride,
        PlayerContext? player,
        CancellationToken cancellationToken)
    {
        if (player is null || string.IsNullOrWhiteSpace(player.PlayerId) || string.IsNullOrWhiteSpace(player.EventInstanceId))
        {
            return null;
        }

        GacBracketLocation? location = await locations
            .FindAsync(allyCode, player.EventInstanceId, player.League, cancellationToken)
            .ConfigureAwait(false);
        if (location is null)
        {
            return null;
        }

        if (formatOverride is GacFormat requestedFormat && requestedFormat != location.Format)
        {
            return null;
        }

        Participant[]? participants = await ReadBracketAsync(location, cancellationToken).ConfigureAwait(false);
        if (participants is null)
        {
            return null;
        }

        int selfIndex = Array.FindIndex(participants, participant =>
            string.Equals(participant.PlayerId, player.PlayerId, StringComparison.Ordinal) || participant.AllyCode == allyCode);
        if (selfIndex < 0)
        {
            return null;
        }

        int? roundNumber = InferRoundNumber(participants);
        Participant? opponent = ResolveFromSwissStandings(participants, player.PlayerId);
        string resolutionMethod = "PersistedBracketPvpScoreRankPairing";
        if (opponent is null && roundNumber == 1)
        {
            opponent = ResolveInitialRoundOpponent(participants, selfIndex);
            resolutionMethod = "PersistedBracketOrderPairing";
        }

        if (opponent is null)
        {
            return null;
        }

        ParticipantProfile? profile = await ResolveProfileAsync(opponent, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        string bracketId = $"{location.EventInstanceId}:{location.League.ToString().ToUpperInvariant()}:{location.BracketIndex}";
        return CurrentGacOpponentLookup.Found(new CurrentGacOpponent(
            allyCode,
            profile.AllyCode,
            profile.Name,
            profile.PlayerId,
            location.League,
            location.Format,
            location.EventId,
            location.EventInstanceId,
            bracketId,
            roundNumber,
            "PersistedBracket",
            resolutionMethod));
    }

    private async Task<PlayerContext?> ReadPlayerContextAsync(long allyCode, CancellationToken cancellationToken)
    {
        using JsonDocument player = await PostAsync(
            "playerArena",
            new
            {
                payload = new { allyCode = allyCode.ToString(), playerDetailsOnly = true },
                enums = false
            },
            cancellationToken).ConfigureAwait(false);

        string? playerId = ReadString(player.RootElement, "playerId") ?? ReadString(player.RootElement, "id");
        int skillRating = ReadNestedInt(player.RootElement, "playerRating", "skillRating") ?? 0;
        if (!TryGetProperty(player.RootElement, "seasonStatus", out JsonElement statuses) || statuses.ValueKind != JsonValueKind.Array)
        {
            return new PlayerContext(playerId, null, GacLeague.Carbonite, skillRating);
        }

        foreach (JsonElement status in statuses.EnumerateArray())
        {
            string? eventInstanceId = ReadString(status, "eventInstanceId");
            if (string.IsNullOrWhiteSpace(eventInstanceId))
            {
                continue;
            }

            GacLeague? league = ReadLeague(status) ?? ReadLeague(player.RootElement);
            if (league is not null)
            {
                return new PlayerContext(playerId, eventInstanceId, league.Value, skillRating);
            }
        }

        return new PlayerContext(playerId, null, ReadLeague(player.RootElement) ?? GacLeague.Carbonite, skillRating);
    }

    private async Task<Participant[]?> ReadBracketAsync(
        GacBracketLocation location,
        CancellationToken cancellationToken)
    {
        string bracketId = $"{location.EventInstanceId}:{location.League.ToString().ToUpperInvariant()}:{location.BracketIndex}";
        HttpClient client = httpClientFactory.CreateClient(SwgohComlinkGacOpponentSource.HttpClientName);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "getLeaderboard",
            new
            {
                payload = new
                {
                    leaderboardType = 4,
                    eventInstanceId = location.EventInstanceId,
                    groupId = bracketId
                },
                enums = false
            },
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!TryGetLeaderboardPlayers(document.RootElement, out JsonElement playersElement))
        {
            return null;
        }

        Participant[] participants =
        [
            .. playersElement.EnumerateArray()
                .Select(ReadParticipant)
                .Where(participant => participant is not null)
                .Select(participant => participant!)
        ];
        return participants.Length == 0 ? null : participants;
    }

    private async Task<ParticipantProfile?> ResolveProfileAsync(
        Participant participant,
        CancellationToken cancellationToken)
    {
        if (participant.AllyCode is long allyCode && allyCode is >= 100_000_000 and <= 999_999_999)
        {
            return new ParticipantProfile(allyCode, participant.Name, participant.PlayerId);
        }

        if (string.IsNullOrWhiteSpace(participant.PlayerId))
        {
            return null;
        }

        using JsonDocument profile = await PostAsync(
            "playerArena",
            new
            {
                payload = new { playerId = participant.PlayerId, playerDetailsOnly = true },
                enums = false
            },
            cancellationToken).ConfigureAwait(false);
        if (!TryReadLongProperty(profile.RootElement, "allyCode", out long resolvedAllyCode) ||
            resolvedAllyCode is < 100_000_000 or > 999_999_999)
        {
            return null;
        }

        return new ParticipantProfile(
            resolvedAllyCode,
            ReadString(profile.RootElement, "name") ?? participant.Name,
            ReadString(profile.RootElement, "playerId") ?? ReadString(profile.RootElement, "id") ?? participant.PlayerId);
    }

    private async Task PersistLocationAsync(
        CurrentGacOpponent opponent,
        int skillRating,
        CancellationToken cancellationToken)
    {
        int separator = opponent.BracketId.LastIndexOf(':');
        if (separator < 0 ||
            !int.TryParse(opponent.BracketId[(separator + 1)..], out int bracketIndex) ||
            bracketIndex < 0)
        {
            return;
        }

        await locations.UpsertAsync(
            new GacBracketLocation(
                opponent.PlayerAllyCode,
                opponent.EventId,
                opponent.EventInstanceId,
                opponent.League,
                opponent.Format,
                bracketIndex,
                skillRating,
                DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> PostAsync(string path, object request, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(SwgohComlinkGacOpponentSource.HttpClientName);
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static Participant? ResolveFromSwissStandings(IReadOnlyCollection<Participant> participants, string playerId)
    {
        Participant? current = participants.FirstOrDefault(participant =>
            string.Equals(participant.PlayerId, playerId, StringComparison.Ordinal));
        if (current is null || !TryReadPvpValue(current.Element, "score", out int currentScore))
        {
            return null;
        }

        Participant[] sameRecord =
        [
            .. participants.Where(participant =>
                TryReadPvpValue(participant.Element, "score", out int score) && score == currentScore)
        ];
        if (sameRecord.Length is not (2 or 4))
        {
            return null;
        }

        RankedParticipant[] ranked =
        [
            .. sameRecord
                .Select(participant => TryReadPvpValue(participant.Element, "rank", out int rank)
                    ? new RankedParticipant(participant, rank)
                    : null)
                .Where(value => value is not null)
                .Select(value => value!)
                .OrderBy(value => value.Rank)
        ];
        if (ranked.Length != sameRecord.Length || ranked.Select(value => value.Rank).Distinct().Count() != ranked.Length)
        {
            return null;
        }

        int currentIndex = Array.FindIndex(ranked, value =>
            string.Equals(value.Participant.PlayerId, playerId, StringComparison.Ordinal));
        if (currentIndex < 0)
        {
            return null;
        }

        int pairedIndex = ranked.Length - 1 - currentIndex;
        return pairedIndex == currentIndex ? null : ranked[pairedIndex].Participant;
    }

    private static Participant? ResolveInitialRoundOpponent(IReadOnlyList<Participant> participants, int playerIndex)
    {
        int opponentIndex = playerIndex % 2 == 0 ? playerIndex + 1 : playerIndex - 1;
        return opponentIndex >= 0 && opponentIndex < participants.Count ? participants[opponentIndex] : null;
    }

    private static int? InferRoundNumber(IReadOnlyCollection<Participant> participants)
    {
        int[] scores =
        [
            .. participants
                .Select(participant => TryReadPvpValue(participant.Element, "score", out int score) ? score : -1)
                .Where(score => score >= 0)
        ];
        return scores.Length == 0 ? null : Math.Clamp(scores.Max() + 1, 1, 3);
    }

    private static bool TryGetLeaderboardPlayers(JsonElement root, out JsonElement players)
    {
        if (TryGetProperty(root, "player", out players) && players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0)
        {
            return true;
        }

        if (TryGetProperty(root, "leaderboard", out JsonElement leaderboards) && leaderboards.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement leaderboard in leaderboards.EnumerateArray())
            {
                if (TryGetProperty(leaderboard, "player", out players) && players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0)
                {
                    return true;
                }
            }
        }

        players = default;
        return false;
    }

    private static Participant? ReadParticipant(JsonElement element)
    {
        string? playerId = ReadString(element, "id") ?? ReadString(element, "playerId");
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        long? allyCode = TryReadLongProperty(element, "allyCode", out long value) ? value : null;
        return new Participant(
            ReadString(element, "name") ?? ReadString(element, "playerName") ?? playerId,
            playerId,
            allyCode,
            element.Clone());
    }

    private static GacLeague? ReadLeague(JsonElement element)
    {
        string? text = ReadString(element, "league");
        if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(text, true, out GacLeague parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        if (TryGetProperty(element, "playerRating", out JsonElement rating))
        {
            text = ReadString(rating, "league");
            if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(text, true, out parsed) && Enum.IsDefined(parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? ReadNestedInt(JsonElement element, string parentName, string propertyName) =>
        TryGetProperty(element, parentName, out JsonElement parent) &&
        TryGetProperty(parent, propertyName, out JsonElement property) &&
        TryReadInt(property, out int value)
            ? value
            : null;

    private static bool TryReadPvpValue(JsonElement participant, string name, out int value)
    {
        value = default;
        return TryGetProperty(participant, "pvpStatus", out JsonElement pvpStatus) &&
               TryGetProperty(pvpStatus, name, out JsonElement property) &&
               TryReadInt(property, out value);
    }

    private static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) ? JsonString(value) : null;

    private static bool TryReadLongProperty(JsonElement element, string name, out long value)
    {
        value = default;
        return TryGetProperty(element, name, out JsonElement property) && long.TryParse(JsonString(property), out value);
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

    private sealed record ResultCacheEntry(CurrentGacOpponentLookup Lookup, DateTimeOffset ExpiresAtUtc);
    private sealed record PlayerContext(string? PlayerId, string? EventInstanceId, GacLeague League, int SkillRating);
    private sealed record Participant(string Name, string PlayerId, long? AllyCode, JsonElement Element);
    private sealed record ParticipantProfile(long AllyCode, string Name, string? PlayerId);
    private sealed record RankedParticipant(Participant Participant, int Rank);
}
