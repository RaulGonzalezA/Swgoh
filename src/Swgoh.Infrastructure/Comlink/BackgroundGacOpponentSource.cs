using System.Diagnostics;
using System.Threading.Channels;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class BackgroundGacOpponentSource(
    ICurrentGacOpponentSource source,
    ILogger<BackgroundGacOpponentSource> logger) : BackgroundService, ICurrentGacOpponentSource
{
    private readonly object gate = new();
    private readonly Dictionary<(long AllyCode, GacFormat? Format), Entry> entries = [];
    private readonly Dictionary<long, long> generations = [];
    private readonly Channel<LookupWorkItem> queue = Channel.CreateBounded<LookupWorkItem>(32);

    public Task<CurrentGacOpponentLookup> GetAsync(long allyCode, GacFormat? formatOverride, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        lock (gate)
        {
            foreach (var expired in entries.Where(item => item.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(item => item.Key).ToArray())
            {
                entries.Remove(expired);
            }

            var key = (allyCode, formatOverride);
            if (entries.TryGetValue(key, out Entry? existing))
            {
                stopwatch.Stop();
                GacTelemetry.RecordOpponentLookup(stopwatch.Elapsed, existing.Result.Status, cacheHit: true, "background-cache");
                return Task.FromResult(existing.Result);
            }

            long generation = generations.GetValueOrDefault(allyCode);
            var pending = CurrentGacOpponentLookup.Unavailable(CurrentGacOpponentStatus.Pending, "Buscando rival de Gran Arena en segundo plano…");
            if (!queue.Writer.TryWrite(new LookupWorkItem(key, generation)))
            {
                CurrentGacOpponentLookup unavailable = CurrentGacOpponentLookup.Unavailable(
                    CurrentGacOpponentStatus.OpponentUnavailable,
                    "Hay demasiadas búsquedas pendientes. Inténtalo más tarde.");
                stopwatch.Stop();
                GacTelemetry.RecordOpponentLookup(stopwatch.Elapsed, unavailable.Status, cacheHit: false, "background-queue-full");
                return Task.FromResult(unavailable);
            }

            entries[key] = new Entry(pending, DateTimeOffset.MaxValue);
            stopwatch.Stop();
            GacTelemetry.RecordOpponentLookup(stopwatch.Elapsed, pending.Status, cacheHit: false, "background-queued");
            return Task.FromResult(pending);
        }
    }

    public void Invalidate(long allyCode)
    {
        lock (gate)
        {
            generations[allyCode] = generations.GetValueOrDefault(allyCode) + 1;
            foreach (var key in entries.Keys.Where(key => key.AllyCode == allyCode).ToArray())
            {
                entries.Remove(key);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (LookupWorkItem workItem in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var key = workItem.Key;
            CurrentGacOpponentLookup result;
            Stopwatch stopwatch = Stopwatch.StartNew();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(6));
            try
            {
                result = await source.GetAsync(key.AllyCode, key.Format, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "GAC background lookup failed for player {AllyCode}", key.AllyCode);
                result = CurrentGacOpponentLookup.Unavailable(
                    CurrentGacOpponentStatus.OpponentUnavailable,
                    "No se ha podido resolver el rival de Gran Arena. Inténtalo de nuevo en un minuto.");
            }
            finally
            {
                stopwatch.Stop();
            }

            GacTelemetry.RecordOpponentLookup(stopwatch.Elapsed, result.Status, cacheHit: false, "provider");
            logger.LogInformation(
                "GAC background provider completed for {AllyCode} with {Status} in {ElapsedMs} ms",
                key.AllyCode,
                result.Status,
                stopwatch.Elapsed.TotalMilliseconds);

            lock (gate)
            {
                long currentGeneration = generations.GetValueOrDefault(key.AllyCode);
                if (currentGeneration != workItem.Generation)
                {
                    logger.LogDebug(
                        "Discarding stale GAC background result for {AllyCode}: generation {CompletedGeneration}, current {CurrentGeneration}",
                        key.AllyCode,
                        workItem.Generation,
                        currentGeneration);
                    continue;
                }

                entries[key] = new Entry(result, DateTimeOffset.UtcNow.AddMinutes(result.Status == CurrentGacOpponentStatus.Found ? 5 : 1));
            }
        }
    }

    private sealed record Entry(CurrentGacOpponentLookup Result, DateTimeOffset ExpiresAt);
    private readonly record struct LookupWorkItem((long AllyCode, GacFormat? Format) Key, long Generation);
}
