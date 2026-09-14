using System.Collections.Concurrent;
using System.Diagnostics;

using Swgoh.Application.Abstractions;
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
    IGacCounterStatisticsService counterStatisticsService,
    IClock? clock = null) : ICurrentGacScoutingService
{
    private const int MaxRounds = 200;
    private const int CounterSourceRoundLimit = 2_000;
    private const int CounterResultLimit = 500;
    private const int TopCharacterLimit = 20;
    private const int TopShipLimit = 12;
    private const int OmicronScoutLimit = 30;
    private static readonly TimeSpan ProfileFreshnessWindow = TimeSpan.FromMinutes(10);

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

        Stopwatch total = Stopwatch.StartNew();
        CurrentGacOpponentStatus finalStatus = CurrentGacOpponentStatus.OpponentUnavailable;
        string? telemetryFormat = formatOverride?.ToString();
        int warningCount = 0;
        using Activity? activity = GacTelemetry.ActivitySource.StartActivity("gac.scouting.pipeline", ActivityKind.Internal);
        activity?.SetTag("gac.ally_code", allyCode);

        try
        {
            CurrentGacOpponentLookup lookup = await GacTelemetry.MeasurePhaseAsync(
                "opponent_lookup",
                () => opponentSource.GetAsync(allyCode, formatOverride, cancellationToken),
                telemetryFormat).ConfigureAwait(false);
            finalStatus = lookup.Status;
            activity?.SetTag("gac.lookup_status", lookup.Status.ToString());

            if (lookup.Status != CurrentGacOpponentStatus.Found || lookup.Opponent is null)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                return new CurrentGacScoutingResult(lookup, null, null, null);
            }

            CurrentGacOpponent opponent = lookup.Opponent;
            telemetryFormat = opponent.Format.ToString();
            activity?.SetTag("gac.format", telemetryFormat);
            activity?.SetTag("gac.league", opponent.League.ToString());
            activity?.SetTag("gac.round", opponent.RoundNumber);

            int historyRoundLimit = Math.Clamp(maxRounds, 1, MaxRounds);
            var warnings = new ConcurrentQueue<string>();

            Task<GacHistorySyncResult?> historySyncTask = GacTelemetry.MeasurePhaseAsync(
                "history_sync",
                () => TryHistorySyncAsync(
                    opponent,
                    historyRoundLimit,
                    warnings,
                    cancellationToken),
                telemetryFormat);
            Task<OpponentScoutingReport?> historicalScoutingTask = GacTelemetry.MeasurePhaseAsync(
                "historical_scouting",
                () => TryHistoricalScoutingAfterSyncAsync(
                    opponent,
                    historyRoundLimit,
                    historySyncTask,
                    warnings,
                    cancellationToken),
                telemetryFormat);
            Task<IReadOnlyCollection<GacCounterStatistics>> counterStatisticsTask = GacTelemetry.MeasurePhaseAsync(
                "counter_statistics",
                () => TryCounterStatisticsAsync(
                    opponent.Format,
                    warnings,
                    cancellationToken),
                telemetryFormat);
            Task<PlayerRosterContext> opponentDataTask = RefreshAndSnapshotAsync(
                opponent.OpponentAllyCode,
                "opponent",
                "rival",
                telemetryFormat,
                warnings,
                cancellationToken);
            Task<PlayerRosterContext> playerDataTask = RefreshAndSnapshotAsync(
                allyCode,
                "player",
                "jugador",
                telemetryFormat,
                warnings,
                cancellationToken);

            await Task.WhenAll(
                historySyncTask,
                historicalScoutingTask,
                counterStatisticsTask,
                opponentDataTask,
                playerDataTask).ConfigureAwait(false);

            GacHistorySyncResult? historySync = await historySyncTask.ConfigureAwait(false);
            OpponentScoutingReport? historicalScouting = await historicalScoutingTask.ConfigureAwait(false);
            IReadOnlyCollection<GacCounterStatistics> counterStatistics = await counterStatisticsTask.ConfigureAwait(false);
            PlayerRosterContext opponentData = await opponentDataTask.ConfigureAwait(false);
            PlayerRosterContext playerData = await playerDataTask.ConfigureAwait(false);

            CurrentOpponentRosterScouting? rosterScouting = GacTelemetry.MeasurePhase(
                "opponent_roster_analysis",
                () => BuildRosterScouting(opponentData.Profile, opponentData.Snapshot),
                telemetryFormat);
            CurrentGacBattlePlan? battlePlan = GacTelemetry.MeasurePhase(
                "battle_plan",
                () => BuildBattlePlan(
                    opponent,
                    playerData.Profile,
                    playerData.Snapshot,
                    rosterScouting,
                    historicalScouting,
                    counterStatistics,
                    warnings),
                telemetryFormat);

            warningCount = warnings.Count;
            activity?.SetTag("gac.warning_count", warningCount);
            activity?.SetTag("gac.degraded", warningCount > 0);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return new CurrentGacScoutingResult(
                lookup,
                historicalScouting,
                rosterScouting,
                battlePlan,
                historySync,
                [.. warnings]);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
        finally
        {
            total.Stop();
            GacTelemetry.RecordPipeline(total.Elapsed, finalStatus, telemetryFormat, warningCount);
        }
    }

    private async Task<PlayerRosterContext> RefreshAndSnapshotAsync(
        long allyCode,
        string role,
        string label,
        string? format,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        PlayerProfile? profile = await GacTelemetry.MeasurePhaseAsync(
            $"{role}_profile_refresh",
            () => RefreshOrFallbackAsync(
                allyCode,
                label,
                warnings,
                cancellationToken),
            format).ConfigureAwait(false);

        PlayerRosterSnapshot? snapshot = await GacTelemetry.MeasurePhaseAsync(
            $"{role}_roster_snapshot",
            () => TrySnapshotAsync(
                allyCode,
                label,
                warnings,
                cancellationToken),
            format).ConfigureAwait(false);

        return new PlayerRosterContext(profile, snapshot);
    }

    private async Task<PlayerProfile?> RefreshOrFallbackAsync(
        long allyCode,
        string label,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        PlayerProfile? persisted = null;
        try
        {
            persisted = await playerProfileService.GetAsync(allyCode, cancellationToken).ConfigureAwait(false);
            if (IsFreshPersistedProfile(persisted))
            {
                return persisted;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A failed cache read must not prevent a live refresh.
        }

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
            if (persisted is not null)
            {
                return persisted;
            }

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

    private bool IsFreshPersistedProfile(PlayerProfile? profile)
    {
        DateTimeOffset now = clock?.UtcNow ?? DateTimeOffset.UtcNow;
        return profile is not null
            && !string.IsNullOrWhiteSpace(profile.PlayerId)
            && profile.UpdatedAtUtc >= now - ProfileFreshnessWindow;
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
        GacFormat format,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await counterStatisticsService.GetAsync(
                new GacCounterStatisticsQuery(
                    format,
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

    private sealed record PlayerRosterContext(PlayerProfile? Profile, PlayerRosterSnapshot? Snapshot);
}
