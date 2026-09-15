using Microsoft.Extensions.Logging;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class SwgohComlinkUnifiedGacOpponentSource(
    SwgohComlinkFastGacOpponentSource locator,
    GacExactBracketResolver exactResolver,
    ILogger<SwgohComlinkUnifiedGacOpponentSource> logger) : ICurrentGacOpponentSource
{
    public async Task<CurrentGacOpponentLookup> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken = default)
    {
        CurrentGacOpponentLookup located = await locator
            .GetAsync(allyCode, formatOverride, cancellationToken)
            .ConfigureAwait(false);
        if (located.Status != CurrentGacOpponentStatus.Found || located.Opponent is null)
        {
            return located;
        }

        CurrentGacOpponent current = located.Opponent;
        try
        {
            CurrentGacOpponent? exact = await exactResolver
                .ResolveLocatedAsync(allyCode, current, cancellationToken)
                .ConfigureAwait(false);
            if (exact is not null)
            {
                if (exact.OpponentAllyCode != current.OpponentAllyCode ||
                    !string.Equals(exact.OpponentResolutionMethod, current.OpponentResolutionMethod, StringComparison.Ordinal))
                {
                    logger.LogInformation(
                        "Exact GAC bracket resolution corrected {AllyCode} opponent from {LocatedOpponent} ({LocatedMethod}) to {ExactOpponent} ({ExactMethod})",
                        allyCode,
                        current.OpponentAllyCode,
                        current.OpponentResolutionMethod,
                        exact.OpponentAllyCode,
                        exact.OpponentResolutionMethod);
                }

                return CurrentGacOpponentLookup.Found(exact);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Exact GAC bracket resolution failed for {AllyCode}; evaluating whether the locator result is safe to keep",
                allyCode);
        }

        // Swiss pairing produced by the locator is safe to retain. Initial bracket order is
        // safe only in round one. Never keep adjacency as a fallback for rounds two or three.
        if (string.Equals(current.OpponentResolutionMethod, "PvpScoreRankPairing", StringComparison.Ordinal) ||
            (current.RoundNumber == 1 &&
             string.Equals(current.OpponentResolutionMethod, "BracketOrderPairing", StringComparison.Ordinal)))
        {
            return located;
        }

        return CurrentGacOpponentLookup.Unavailable(
            CurrentGacOpponentStatus.OpponentUnavailable,
            "Se ha localizado el bracket, pero no se ha podido validar con seguridad el rival de la ronda actual.");
    }
}
