using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface ICurrentGacScoutingService
{
    Task<CurrentGacScoutingResult> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        int maxRounds,
        CancellationToken cancellationToken = default);
}

internal sealed class CurrentGacScoutingService(
    ICurrentGacOpponentSource opponentSource,
    IOpponentScoutingService scoutingService) : ICurrentGacScoutingService
{
    private const int MaxRounds = 200;

    public async Task<CurrentGacScoutingResult> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        int maxRounds,
        CancellationToken cancellationToken = default)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

        if (formatOverride is GacFormat format && !Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(formatOverride), formatOverride, "Unsupported GAC format.");
        }

        CurrentGacOpponentLookup lookup = await opponentSource
            .GetAsync(allyCode, formatOverride, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Status != CurrentGacOpponentStatus.Found || lookup.Opponent is null)
        {
            return new CurrentGacScoutingResult(lookup, null);
        }

        CurrentGacOpponent opponent = lookup.Opponent;
        OpponentScoutingReport? scouting = await scoutingService.GetAsync(
            opponent.OpponentAllyCode,
            opponent.Format,
            opponent.League,
            Math.Clamp(maxRounds, 1, MaxRounds),
            cancellationToken).ConfigureAwait(false);

        return new CurrentGacScoutingResult(lookup, scouting);
    }
}
