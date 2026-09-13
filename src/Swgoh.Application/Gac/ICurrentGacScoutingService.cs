using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

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
            return new CurrentGacScoutingResult(lookup, null, null, null);
        }

        CurrentGacOpponent opponent = lookup.Opponent;
        PlayerProfile refreshedOpponent = await playerProfileService
            .RefreshFromGameAsync(opponent.OpponentAllyCode, cancellationToken)
            .ConfigureAwait(false);
        PlayerProfile refreshedPlayer = await playerProfileService
            .RefreshFromGameAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);

        PlayerRosterAnalysis opponentAnalysis = PlayerRosterMetrics.Calculate(refreshedOpponent);
        PlayerRosterAnalysis playerAnalysis = PlayerRosterMetrics.Calculate(refreshedPlayer);

        Task<OpponentScoutingReport?> historicalScoutingTask = scoutingService.GetAsync(
            opponent.OpponentAllyCode,
            opponent.Format,
            opponent.League,
            Math.Clamp(maxRounds, 1, MaxRounds),
            cancellationToken);

        Task<PlayerRosterPage?> opponentCharactersTask = LoadRosterAsync(
            opponent.OpponentAllyCode,
            PlayerRosterUnitType.Character,
            CharacterScoutLimit,
            hasOmicron: null,
            cancellationToken);
        Task<PlayerRosterPage?> opponentShipsTask = LoadRosterAsync(
            opponent.OpponentAllyCode,
            PlayerRosterUnitType.Ship,
            ShipScoutLimit,
            hasOmicron: null,
            cancellationToken);
        Task<PlayerRosterPage?> opponentOmicronsTask = LoadRosterAsync(
            opponent.OpponentAllyCode,
            PlayerRosterUnitType.Character,
            OmicronScoutLimit,
            hasOmicron: true,
            cancellationToken);

        Task<PlayerRosterPage?> playerCharactersTask = LoadRosterAsync(
            allyCode,
            PlayerRosterUnitType.Character,
            CharacterScoutLimit,
            hasOmicron: null,
            cancellationToken);
        Task<PlayerRosterPage?> playerShipsTask = LoadRosterAsync(
            allyCode,
            PlayerRosterUnitType.Ship,
            ShipScoutLimit,
            hasOmicron: null,
            cancellationToken);
        Task<PlayerRosterPage?> playerOmicronsTask = LoadRosterAsync(
            allyCode,
            PlayerRosterUnitType.Character,
            OmicronScoutLimit,
            hasOmicron: true,
            cancellationToken);

        await Task.WhenAll(
            historicalScoutingTask,
            opponentCharactersTask,
            opponentShipsTask,
            opponentOmicronsTask,
            playerCharactersTask,
            playerShipsTask,
            playerOmicronsTask).ConfigureAwait(false);

        OpponentScoutingReport? historicalScouting = await historicalScoutingTask.ConfigureAwait(false);
        PlayerRosterPage? opponentCharacters = await opponentCharactersTask.ConfigureAwait(false);
        PlayerRosterPage? opponentShips = await opponentShipsTask.ConfigureAwait(false);
        PlayerRosterPage? opponentOmicrons = await opponentOmicronsTask.ConfigureAwait(false);
        PlayerRosterPage? playerCharacters = await playerCharactersTask.ConfigureAwait(false);
        PlayerRosterPage? playerShips = await playerShipsTask.ConfigureAwait(false);
        PlayerRosterPage? playerOmicrons = await playerOmicronsTask.ConfigureAwait(false);

        CurrentOpponentRosterScouting rosterScouting = new(
            opponentAnalysis,
            [.. (opponentCharacters?.Items ?? []).Where(IsGalacticLegend)],
            [.. (opponentCharacters?.Items ?? []).Take(TopCharacterLimit)],
            [.. (opponentShips?.Items ?? []).Take(TopShipLimit)],
            [.. (opponentOmicrons?.Items ?? [])]);

        CurrentGacBattlePlan battlePlan = CurrentGacBattlePlanBuilder.Build(
            opponent,
            playerAnalysis,
            playerCharacters?.Items ?? [],
            playerShips?.Items ?? [],
            playerOmicrons?.Items ?? [],
            rosterScouting,
            historicalScouting);

        return new CurrentGacScoutingResult(lookup, historicalScouting, rosterScouting, battlePlan);
    }

    private Task<PlayerRosterPage?> LoadRosterAsync(
        long allyCode,
        PlayerRosterUnitType type,
        int pageSize,
        bool? hasOmicron,
        CancellationToken cancellationToken) => playerRosterService.GetAsync(
        allyCode,
        new PlayerRosterQuery(
            PageSize: pageSize,
            Type: type,
            HasOmicron: hasOmicron,
            OrderBy: PlayerRosterSortField.GalacticPower,
            Direction: PlayerRosterSortDirection.Descending),
        cancellationToken);

    private static bool IsGalacticLegend(PlayerRosterUnit unit) =>
        unit.Tags.Any(tag => tag.Contains("galactic_legend", StringComparison.OrdinalIgnoreCase));
}
