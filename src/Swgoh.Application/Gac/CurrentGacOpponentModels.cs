using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public enum CurrentGacOpponentStatus
{
    Found = 1,
    NoActiveEvent = 2,
    PlayerNotJoined = 3,
    OpponentUnavailable = 4,
    FormatUnavailable = 5
}

public sealed record CurrentGacOpponent(
    long PlayerAllyCode,
    long OpponentAllyCode,
    string OpponentName,
    string? OpponentPlayerId,
    GacLeague League,
    GacFormat Format,
    string EventId,
    string EventInstanceId,
    string BracketId,
    int? RoundNumber,
    string FormatSource,
    string OpponentResolutionMethod);

public sealed record CurrentGacOpponentLookup(
    CurrentGacOpponentStatus Status,
    CurrentGacOpponent? Opponent,
    string? Message)
{
    public static CurrentGacOpponentLookup Found(CurrentGacOpponent opponent)
    {
        ArgumentNullException.ThrowIfNull(opponent);
        return new CurrentGacOpponentLookup(CurrentGacOpponentStatus.Found, opponent, null);
    }

    public static CurrentGacOpponentLookup Unavailable(
        CurrentGacOpponentStatus status,
        string message) => new(status, null, message);
}

public sealed record CurrentGacScoutingResult(
    CurrentGacOpponentLookup Lookup,
    OpponentScoutingReport? Scouting);
