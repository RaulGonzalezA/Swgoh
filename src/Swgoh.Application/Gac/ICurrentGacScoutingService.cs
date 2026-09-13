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
    IPlayerRosterService playerRosterService,
    IGacHistorySyncService historySyncService,
    IGacCounterStatisticsService counterStatisticsService) : ICurrentGacScoutingService
{
    private const int MaxRounds = 200;
    private const int CounterSourceRoundLimit = 2_000;
    private const int CounterResultLimit = 500;
    private const int CharacterScoutLimit = 100;
    private const int ShipScoutLimit = 50;
    private const int RosterPageSize = 100;
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
        int historyRoundLimit = Math.Clamp(maxRounds, 1, MaxRounds);
        GacHistorySyncResult historySync = await historySyncService
            .SyncAsync(opponent.OpponentAllyCode, opponent.Format, historyRoundLimit, cancellationToken)
            .ConfigureAwait(false);

        Task<PlayerProfile> refreshedOpponentTask = playerProfileService
            .RefreshFromGameAsync(opponent.OpponentAllyCode, cancellationToken);
        Task<PlayerProfile> refreshedPlayerTask = playerProfileService
            .RefreshFromGameAsync(allyCode, cancellationToken);
        await Task.WhenAll(refreshedOpponentTask, refreshedPlayerTask).ConfigureAwait(false);

        PlayerProfile refreshedOpponent = await refreshedOpponentTask.ConfigureAwait(false);
        PlayerProfile refreshedPlayer = await refreshedPlayerTask.ConfigureAwait(false);
        PlayerRosterAnalysis opponentAnalysis = PlayerRosterMetrics.Calculate(refreshedOpponent);
        PlayerRosterAnalysis playerAnalysis = PlayerRosterMetrics.Calculate(refreshedPlayer);

        Task<OpponentScoutingReport?> historicalScoutingTask = scoutingService.GetAsync(
            opponent.OpponentAllyCode,
            opponent.Format,
            opponent.League,
            historyRoundLimit,
            cancellationToken);
        Task<IReadOnlyCollection<GacCounterStatistics>> counterStatisticsTask = counterStatisticsService.GetAsync(
            new GacCounterStatisticsQuery(
                opponent.Format,
                MaxRounds: CounterSourceRoundLimit,
                Limit: CounterResultLimit),
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

        Task<IReadOnlyCollection<PlayerRosterUnit>> playerCharactersTask = LoadFullRosterAsync(
            allyCode,
            PlayerRosterUnitType.Character,
            cancellationToken);
        Task<IReadOnlyCollection<PlayerRosterUnit>> playerShipsTask = LoadFullRosterAsync(
            allyCode,
            PlayerRosterUnitType.Ship,
            cancellationToken);
        Task<PlayerRosterPage?> playerOmicronsTask = LoadRosterAsync(
            allyCode,
            PlayerRosterUnitType.Character,
            OmicronScoutLimit,
            hasOmicron: true,
            cancellationToken);

        await Task.WhenAll(
            historicalScoutingTask,
            counterStatisticsTask,
            opponentCharactersTask,
            opponentShipsTask,
            opponentOmicronsTask,
            playerCharactersTask,
            playerShipsTask,
            playerOmicronsTask).ConfigureAwait(false);

        OpponentScoutingReport? historicalScouting = await historicalScoutingTask.ConfigureAwait(false);
        IReadOnlyCollection<GacCounterStatistics> counterStatistics = await counterStatisticsTask.ConfigureAwait(false);
        PlayerRosterPage? opponentCharacters = await opponentCharactersTask.ConfigureAwait(false);
        PlayerRosterPage? opponentShips = await opponentShipsTask.ConfigureAwait(false);
        PlayerRosterPage? opponentOmicrons = await opponentOmicronsTask.ConfigureAwait(false);
        IReadOnlyCollection<PlayerRosterUnit> playerCharacters = await playerCharactersTask.ConfigureAwait(false);
        IReadOnlyCollection<PlayerRosterUnit> playerShips = await playerShipsTask.ConfigureAwait(false);
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
            playerCharacters,
            playerShips,
            playerOmicrons?.Items ?? [],
            rosterScouting,
            historicalScouting);
        battlePlan = GacCounterSuggestionEnricher.Enrich(
            battlePlan,
            opponent.Format,
            counterStatistics,
            playerCharacters,
            playerShips);

        return new CurrentGacScoutingResult(
            lookup,
            historicalScouting,
            rosterScouting,
            battlePlan,
            historySync);
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

    private async Task<IReadOnlyCollection<PlayerRosterUnit>> LoadFullRosterAsync(
        long allyCode,
        PlayerRosterUnitType type,
        CancellationToken cancellationToken)
    {
        PlayerRosterPage? firstPage = await playerRosterService.GetAsync(
            allyCode,
            new PlayerRosterQuery(
                Page: 1,
                PageSize: RosterPageSize,
                Type: type,
                OrderBy: PlayerRosterSortField.GalacticPower,
                Direction: PlayerRosterSortDirection.Descending),
            cancellationToken).ConfigureAwait(false);
        if (firstPage is null)
        {
            return [];
        }

        var units = new List<PlayerRosterUnit>(firstPage.Items);
        for (int page = 2; page <= firstPage.TotalPages; page++)
        {
            PlayerRosterPage? nextPage = await playerRosterService.GetAsync(
                allyCode,
                new PlayerRosterQuery(
                    Page: page,
                    PageSize: RosterPageSize,
                    Type: type,
                    OrderBy: PlayerRosterSortField.GalacticPower,
                    Direction: PlayerRosterSortDirection.Descending),
                cancellationToken).ConfigureAwait(false);
            if (nextPage is null)
            {
                break;
            }

            units.AddRange(nextPage.Items);
        }

        return units;
    }

    private static bool IsGalacticLegend(PlayerRosterUnit unit) =>
        unit.Tags.Any(tag => tag.Contains("galactic_legend", StringComparison.OrdinalIgnoreCase));
}
