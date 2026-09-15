using System.Net;
using System.Text.Json;

using Swgoh.Application.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class GacExactBracketResolver(IComlinkGacClient client)
{
    public async Task<CurrentGacOpponent?> ResolveLocatedAsync(
        long allyCode,
        CurrentGacOpponent located,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(located);

        ResolutionContext? context = await ResolveAsync(
            allyCode,
            located.EventId,
            located.EventInstanceId,
            located.BracketId,
            located.RoundNumber,
            methodPrefix: string.Empty,
            cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        return located with
        {
            OpponentAllyCode = context.Opponent.AllyCode,
            OpponentName = context.Opponent.Name,
            OpponentPlayerId = context.Opponent.PlayerId,
            RoundNumber = context.RoundNumber,
            OpponentResolutionMethod = context.ResolutionMethod
        };
    }

    public async Task<CurrentGacOpponent?> ResolvePersistedAsync(
        long allyCode,
        GacBracketLocation location,
        Swgoh.Domain.Gac.GacFormat? formatOverride,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (formatOverride is Swgoh.Domain.Gac.GacFormat requestedFormat && requestedFormat != location.Format)
        {
            return null;
        }

        using JsonDocument events = await client.GetEventsAsync(cancellationToken).ConfigureAwait(false);
        GacActiveEvent? activeEvent = GacBracketParser.ReadActiveEvent(events.RootElement);
        if (activeEvent is null ||
            !string.Equals(location.EventId, activeEvent.EventId, StringComparison.Ordinal) ||
            !string.Equals(location.EventInstanceId, activeEvent.EventInstanceId, StringComparison.Ordinal))
        {
            return null;
        }

        string bracketId = $"{location.EventInstanceId}:{location.League.ToString().ToUpperInvariant()}:{location.BracketIndex}";
        int? roundNumber = GacBracketParser.ReadRoundNumber(activeEvent.InstanceElement) ??
            GacBracketParser.ReadRoundNumber(activeEvent.EventElement);
        ResolutionContext? context = await ResolveExactBracketAsync(
            allyCode,
            location.EventInstanceId,
            bracketId,
            roundNumber,
            methodPrefix: "PersistedBracket",
            cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        return new CurrentGacOpponent(
            allyCode,
            context.Opponent.AllyCode,
            context.Opponent.Name,
            context.Opponent.PlayerId,
            location.League,
            location.Format,
            location.EventId,
            location.EventInstanceId,
            bracketId,
            context.RoundNumber,
            "PersistedBracket",
            context.ResolutionMethod);
    }

    public async Task<int> ReadSkillRatingAsync(long allyCode, CancellationToken cancellationToken)
    {
        using JsonDocument player = await client.GetPlayerByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return GacBracketParser.ReadSkillRating(player.RootElement) ?? 0;
    }

    private async Task<ResolutionContext?> ResolveAsync(
        long allyCode,
        string eventId,
        string eventInstanceId,
        string bracketId,
        int? roundNumber,
        string methodPrefix,
        CancellationToken cancellationToken)
    {
        using JsonDocument events = await client.GetEventsAsync(cancellationToken).ConfigureAwait(false);
        GacActiveEvent? activeEvent = GacBracketParser.ReadActiveEvent(events.RootElement);
        if (activeEvent is not null &&
            string.Equals(eventId, activeEvent.EventId, StringComparison.Ordinal) &&
            string.Equals(eventInstanceId, activeEvent.EventInstanceId, StringComparison.Ordinal))
        {
            roundNumber ??= GacBracketParser.ReadRoundNumber(activeEvent.InstanceElement) ??
                GacBracketParser.ReadRoundNumber(activeEvent.EventElement);
        }

        return await ResolveExactBracketAsync(
            allyCode,
            eventInstanceId,
            bracketId,
            roundNumber,
            methodPrefix,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResolutionContext?> ResolveExactBracketAsync(
        long allyCode,
        string eventInstanceId,
        string bracketId,
        int? roundNumber,
        string methodPrefix,
        CancellationToken cancellationToken)
    {
        using JsonDocument self = await client.GetPlayerByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);
        string? playerId = GacBracketParser.ReadPlayerId(self.RootElement);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        ComlinkGacResponse leaderboard = await client
            .GetLeaderboardAsync(eventInstanceId, bracketId, cancellationToken)
            .ConfigureAwait(false);
        if (leaderboard.StatusCode == HttpStatusCode.BadRequest)
        {
            return null;
        }

        if (!leaderboard.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Comlink returned HTTP {(int)leaderboard.StatusCode} while reading GAC bracket {bracketId}.",
                inner: null,
                leaderboard.StatusCode);
        }

        IReadOnlyList<GacBracketParticipant> participants = GacBracketParser.ReadParticipants(leaderboard.Body);
        if (participants.Count == 0)
        {
            return null;
        }

        int playerIndex = GacBracketParser.FindPlayerIndex(participants, playerId, allyCode);
        if (playerIndex < 0)
        {
            return null;
        }

        roundNumber ??= GacOpponentResolver.InferRoundNumber(participants);
        GacBracketParticipant? opponent = GacOpponentResolver.Resolve(
            participants,
            playerIndex,
            playerId,
            allyCode,
            roundNumber,
            methodPrefix,
            out string resolutionMethod);
        if (opponent is null)
        {
            return null;
        }

        OpponentProfile? profile = await ResolveProfileAsync(opponent, cancellationToken).ConfigureAwait(false);
        return profile is null
            ? null
            : new ResolutionContext(profile, roundNumber, resolutionMethod);
    }

    private async Task<OpponentProfile?> ResolveProfileAsync(
        GacBracketParticipant participant,
        CancellationToken cancellationToken)
    {
        if (participant.AllyCode is long participantAllyCode &&
            GacBracketParser.IsValidAllyCode(participantAllyCode))
        {
            return new OpponentProfile(participantAllyCode, participant.Name, participant.PlayerId);
        }

        if (string.IsNullOrWhiteSpace(participant.PlayerId))
        {
            return null;
        }

        using JsonDocument profile = await client
            .GetPlayerByIdAsync(participant.PlayerId, cancellationToken)
            .ConfigureAwait(false);
        long? resolvedAllyCode = GacBracketParser.ReadAllyCode(profile.RootElement);
        if (resolvedAllyCode is null || !GacBracketParser.IsValidAllyCode(resolvedAllyCode.Value))
        {
            return null;
        }

        return new OpponentProfile(
            resolvedAllyCode.Value,
            GacBracketParser.ReadPlayerName(profile.RootElement, participant.Name),
            GacBracketParser.ReadPlayerId(profile.RootElement) ?? participant.PlayerId);
    }

    private sealed record OpponentProfile(long AllyCode, string Name, string? PlayerId);
    private sealed record ResolutionContext(OpponentProfile Opponent, int? RoundNumber, string ResolutionMethod);
}
