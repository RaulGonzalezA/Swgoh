using Swgoh.Application.Players;
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
    IOpponentScoutingService scoutingService,
    IPlayerProfileService playerProfileService,
    IPlayerRosterService playerRosterService) : ICurrentGacScoutingService
{
    private const int MaxRounds = 200;
    private const int CharacterScoutLimit = 100;
    private const int ShipScoutLimit = 50;
    private const int TopCharacterLimit = 20;
    private const int TopShipLimit = 12;
    private const int OmicronScoutLimit = 30;

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
            return new CurrentGacScoutingResult(lookup, null, null);
        }

        CurrentGacOpponent opponent = lookup.Opponent;
        var refreshedOpponent = await playerProfileService
            .RefreshFromGameAsync(opponent.OpponentAllyCode, cancellationToken)
            .ConfigureAwait(false);
        PlayerRosterAnalysis analysis = PlayerRosterMetrics.Calculate(refreshedOpponent);

        Task<OpponentScoutingReport?> historicalScoutingTask = scoutingService.GetAsync(
            opponent.OpponentAllyCode,
            opponent.Format,
            opponent.League,
            Math.Clamp(maxRounds, 1, MaxRounds),
            cancellationToken);
        Task<PlayerRosterPage?> charactersTask = playerRosterService.GetAsync(
            opponent.OpponentAllyCode,
            new PlayerRosterQuery(
                PageSize: CharacterScoutLimit,
                Type: PlayerRosterUnitType.Character,
                OrderBy: PlayerRosterSortField.GalacticPower,
                Direction: PlayerRosterSortDirection.Descending),
            cancellationToken);
        Task<PlayerRosterPage?> shipsTask = playerRosterService.GetAsync(
            opponent.OpponentAllyCode,
            new PlayerRosterQuery(
                PageSize: ShipScoutLimit,
                Type: PlayerRosterUnitType.Ship,
                OrderBy: PlayerRosterSortField.GalacticPower,
                Direction: PlayerRosterSortDirection.Descending),
            cancellationToken);
        Task<PlayerRosterPage?> omicronsTask = playerRosterService.GetAsync(
            opponent.OpponentAllyCode,
            new PlayerRosterQuery(
                PageSize: OmicronScoutLimit,
                Type: PlayerRosterUnitType.Character,
                HasOmicron: true,
                OrderBy: PlayerRosterSortField.GalacticPower,
                Direction: PlayerRosterSortDirection.Descending),
            cancellationToken);

        await Task.WhenAll(historicalScoutingTask, charactersTask, shipsTask, omicronsTask).ConfigureAwait(false);

        OpponentScoutingReport? historicalScouting = await historicalScoutingTask.ConfigureAwait(false);
        PlayerRosterPage? characters = await charactersTask.ConfigureAwait(false);
        PlayerRosterPage? ships = await shipsTask.ConfigureAwait(false);
        PlayerRosterPage? omicrons = await omicronsTask.ConfigureAwait(false);

        CurrentOpponentRosterScouting rosterScouting = new(
            analysis,
            [.. (characters?.Items ?? []).Where(IsGalacticLegend)],
            [.. (characters?.Items ?? []).Take(TopCharacterLimit)],
            [.. (ships?.Items ?? []).Take(TopShipLimit)],
            [.. (omicrons?.Items ?? [])]);

        return new CurrentGacScoutingResult(lookup, historicalScouting, rosterScouting);
    }

    private static bool IsGalacticLegend(PlayerRosterUnit unit) =>
        unit.Tags.Any(tag => tag.Contains("galactic_legend", StringComparison.OrdinalIgnoreCase));
}
