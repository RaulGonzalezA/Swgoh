using System.Collections.Concurrent;

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
        var warnings = new ConcurrentQueue<string>();

        Task<GacHistorySyncResult?> historySyncTask = TryHistorySyncAsync(
            opponent,
            historyRoundLimit,
            warnings,
            cancellationToken);
        Task<IReadOnlyCollection<GacCounterStatistics>> counterStatisticsTask = TryCounterStatisticsAsync(
            warnings,
            cancellationToken);
        Task<PlayerProfile?> refreshedOpponentTask = RefreshOrFallbackAsync(
            opponent.OpponentAllyCode,
            "rival",
            warnings,
            cancellationToken);
        Task<PlayerProfile?> refreshedPlayerTask = RefreshOrFallbackAsync(
            allyCode,
            "jugador",
            warnings,
            cancellationToken);

        await Task.WhenAll(refreshedOpponentTask, refreshedPlayerTask).ConfigureAwait(false);

        PlayerProfile? refreshedOpponent = await refreshedOpponentTask.ConfigureAwait(false);
        PlayerProfile? refreshedPlayer = await refreshedPlayerTask.ConfigureAwait(false);

        Task<PlayerRosterSnapshot?> opponentSnapshotTask = TrySnapshotAsync(
            opponent.OpponentAllyCode,
            "rival",
            warnings,
            cancellationToken);
        Task<PlayerRosterSnapshot?> playerSnapshotTask = TrySnapshotAsync(
            allyCode,
            "jugador",
            warnings,
            cancellationToken);
        Task<OpponentScoutingReport?> historicalScoutingTask = TryHistoricalScoutingAfterSyncAsync(
            opponent,
            historyRoundLimit,
            historySyncTask,
            warnings,
            cancellationToken);

        await Task.WhenAll(
            historySyncTask,
            counterStatisticsTask,
            opponentSnapshotTask,
            playerSnapshotTask,
            historicalScoutingTask).ConfigureAwait(false);

        GacHistorySyncResult? historySync = await historySyncTask.ConfigureAwait(false);
        IReadOnlyCollection<GacCounterStatistics> counterStatistics = await counterStatisticsTask.ConfigureAwait(false);
        PlayerRosterSnapshot? opponentSnapshot = await opponentSnapshotTask.ConfigureAwait(false);
        PlayerRosterSnapshot? playerSnapshot = await playerSnapshotTask.ConfigureAwait(false);
        OpponentScoutingReport? historicalScouting = await historicalScoutingTask.ConfigureAwait(false);

        CurrentOpponentRosterScouting? rosterScouting = BuildRosterScouting(refreshedOpponent, opponentSnapshot);
        CurrentGacBattlePlan? battlePlan = BuildBattlePlan(
            opponent,
            refreshedPlayer,
            playerSnapshot,
            rosterScouting,
            historicalScouting,
            counterStatistics,
            warnings);

        return new CurrentGacScoutingResult(
            lookup,
            historicalScouting,
            rosterScouting,
            battlePlan,
            historySync,
            [.. warnings]);
    }

    private async Task<PlayerProfile?> RefreshOrFallbackAsync(
        long allyCode,
        string label,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await playerProfileService.RefreshFromGameAsync(allyCode, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            warnings.Enqueue($"No se ha podido refrescar el perfil del {label}; se usarán los últimos datos persistidos disponibles.");
            try
            {
                return await playerProfileService.GetAsync(allyCode, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                warnings.Enqueue($"Tampoco se ha podido recuperar el perfil persistido del {label}.");
                return null;
            }
        }
    }

    private async Task<PlayerRosterSnapshot?> TrySnapshotAsync(
        long allyCode,
        string label,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            PlayerRosterSnapshot? snapshot = await playerRosterService
                .GetSnapshotAsync(allyCode, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                warnings.Enqueue($"No hay snapshot de roster disponible para el {label}.");
            }

            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            warnings.Enqueue($"No se ha podido preparar el snapshot de roster del {label}.");
            return null;
        }
    }

    private async Task<GacHistorySyncResult?> TryHistorySyncAsync(
        CurrentGacOpponent opponent,
        int historyRoundLimit,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await historySyncService
                .SyncAsync(opponent.OpponentAllyCode, opponent.Format, historyRoundLimit, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            warnings.Enqueue("No se ha podido actualizar el histórico de GAC; se continuará con los datos ya almacenados.");
            return null;
        }
    }

    private async Task<OpponentScoutingReport?> TryHistoricalScoutingAfterSyncAsync(
        CurrentGacOpponent opponent,
        int historyRoundLimit,
        Task<GacHistorySyncResult?> historySyncTask,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        await historySyncTask.ConfigureAwait(false);
        try
        {
            return await scoutingService.GetAsync(
                opponent.OpponentAllyCode,
                opponent.Format,
                opponent.League,
                historyRoundLimit,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            warnings.Enqueue("No se ha podido calcular el scouting histórico del rival.");
            return null;
        }
    }

    private async Task<IReadOnlyCollection<GacCounterStatistics>> TryCounterStatisticsAsync(
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await counterStatisticsService.GetAsync(
                new GacCounterStatisticsQuery(
                    Format: null,
                    MaxRounds: CounterSourceRoundLimit,
                    Limit: CounterResultLimit),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            warnings.Enqueue("No se han podido cargar las estadísticas globales de counters; se usarán recomendaciones locales.");
            return [];
        }
    }

    private static CurrentOpponentRosterScouting? BuildRosterScouting(
        PlayerProfile? opponentProfile,
        PlayerRosterSnapshot? opponentSnapshot)
    {
        if (opponentProfile is null || opponentSnapshot is null)
        {
            return null;
        }

        PlayerRosterUnit[] characters =
        [
            .. opponentSnapshot.Units
                .Where(unit => !unit.IsShip)
                .OrderByDescending(unit => unit.GalacticPower)
        ];
        PlayerRosterUnit[] ships =
        [
            .. opponentSnapshot.Units
                .Where(unit => unit.IsShip)
                .OrderByDescending(unit => unit.GalacticPower)
        ];

        return new CurrentOpponentRosterScouting(
            PlayerRosterMetrics.Calculate(opponentProfile),
            [.. characters.Where(IsGalacticLegend)],
            [.. characters.Take(TopCharacterLimit)],
            [.. ships.Take(TopShipLimit)],
            [.. characters.Where(unit => unit.OmicronCount > 0).Take(OmicronScoutLimit)]);
    }

    private static CurrentGacBattlePlan? BuildBattlePlan(
        CurrentGacOpponent opponent,
        PlayerProfile? playerProfile,
        PlayerRosterSnapshot? playerSnapshot,
        CurrentOpponentRosterScouting? rosterScouting,
        OpponentScoutingReport? historicalScouting,
        IReadOnlyCollection<GacCounterStatistics> counterStatistics,
        ConcurrentQueue<string> warnings)
    {
        if (playerProfile is null || playerSnapshot is null || rosterScouting is null)
        {
            warnings.Enqueue("El rival está identificado, pero faltan datos de roster para construir el plan de batalla completo.");
            return null;
        }

        try
        {
            PlayerRosterUnit[] playerCharacters =
            [
                .. playerSnapshot.Units
                    .Where(unit => !unit.IsShip)
                    .OrderByDescending(unit => unit.GalacticPower)
            ];
            PlayerRosterUnit[] playerShips =
            [
                .. playerSnapshot.Units
                    .Where(unit => unit.IsShip)
                    .OrderByDescending(unit => unit.GalacticPower)
            ];
            PlayerRosterUnit[] playerOmicrons =
            [
                .. playerCharacters
                    .Where(unit => unit.OmicronCount > 0)
                    .Take(OmicronScoutLimit)
            ];

            CurrentGacBattlePlan battlePlan = CurrentGacBattlePlanBuilder.Build(
                opponent,
                PlayerRosterMetrics.Calculate(playerProfile),
                playerCharacters,
                playerShips,
                playerOmicrons,
                rosterScouting,
                historicalScouting);

            return GacCounterSuggestionEnricher.Enrich(
                battlePlan,
                opponent.Format,
                counterStatistics,
                playerCharacters,
                playerShips);
        }
        catch (Exception)
        {
            warnings.Enqueue("El rival está identificado, pero no se ha podido construir el plan de batalla completo.");
            return null;
        }
    }

    private static bool IsGalacticLegend(PlayerRosterUnit unit) =>
        unit.Tags.Any(tag => tag.Contains("galactic_legend", StringComparison.OrdinalIgnoreCase));
}
