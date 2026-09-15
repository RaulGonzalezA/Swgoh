using System.Diagnostics;
using System.Threading.Channels;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Swgoh.Application.Caching;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class BackgroundGacOpponentSource(
    ILiveGacOpponentSource source,
    IGacTelemetry gacTelemetry,
    ILogger<BackgroundGacOpponentSource> logger) : BackgroundService, ICurrentGacOpponentSource
{
    internal const int WorkerCount = 3;
    private const long EntryCacheSizeLimit = 512;
    private const long GenerationCacheSizeLimit = 2_048;
    private static readonly TimeSpan GenerationCacheDuration = TimeSpan.FromHours(2);
    private static readonly TimeSpan PendingEntryDuration = TimeSpan.FromMinutes(7);

    private readonly object gate = new();
    private readonly BoundedMemoryCache<(long AllyCode, GacFormat? Format), Entry> entries = new(
        EntryCacheSizeLimit,
        absoluteExpirationSelector: static entry => entry.ExpiresAt);
    private readonly BoundedMemoryCache<long, long> generations = new(
        GenerationCacheSizeLimit,
        defaultLifetime: GenerationCacheDuration);
    private readonly Channel<LookupWorkItem> queue = Channel.CreateBounded<LookupWorkItem>(new BoundedChannelOptions(32)
    {
        SingleReader = false,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private long generationSequence;
    private int disposed;

    public Task<CurrentGacOpponentLookup> GetAsync(long allyCode, GacFormat? formatOverride, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(allyCode, 100_000_000L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(allyCode, 999_999_999L);

        Stopwatch stopwatch = Stopwatch.StartNew();
        lock (gate)
        {
            var key = (allyCode, formatOverride);
            if (entries.TryGetValue(key, out Entry? existing))
            {
                stopwatch.Stop();
                gacTelemetry.RecordOpponentLookup(stopwatch.Elapsed, existing.Result.Status, cacheHit: true, "background-cache");
                return Task.FromResult(existing.Result);
            }

            long generation = GetGeneration(allyCode);
            var pending = CurrentGacOpponentLookup.Unavailable(CurrentGacOpponentStatus.Pending, "Buscando rival de Gran Arena en segundo plano…");
            if (!queue.Writer.TryWrite(new LookupWorkItem(key, generation)))
            {
                CurrentGacOpponentLookup unavailable = CurrentGacOpponentLookup.Unavailable(
                    CurrentGacOpponentStatus.OpponentUnavailable,
                    "Hay demasiadas búsquedas pendientes. Inténtalo más tarde.");
                stopwatch.Stop();
                gacTelemetry.RecordOpponentLookup(stopwatch.Elapsed, unavailable.Status, cacheHit: false, "background-queue-full");
                return Task.FromResult(unavailable);
            }

            entries[key] = new Entry(pending, DateTimeOffset.UtcNow.Add(PendingEntryDuration));
            stopwatch.Stop();
            gacTelemetry.RecordOpponentLookup(stopwatch.Elapsed, pending.Status, cacheHit: false, "background-queued");
            return Task.FromResult(pending);
        }
    }

    public void Invalidate(long allyCode)
    {
        lock (gate)
        {
            generations[allyCode] = ++generationSequence;
            foreach (var key in entries.Keys.Where(key => key.AllyCode == allyCode).ToArray())
            {
                entries.TryRemove(key, out _);
            }
        }
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            entries.Dispose();
            generations.Dispose();
        }

        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task[] workers = Enumerable.Range(0, WorkerCount)
            .Select(workerId => ProcessQueueAsync(workerId, stoppingToken))
            .ToArray();
        return Task.WhenAll(workers);
    }

    private async Task ProcessQueueAsync(int workerId, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (LookupWorkItem workItem in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessWorkItemAsync(workerId, workItem, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
    }

    private async Task ProcessWorkItemAsync(int workerId, LookupWorkItem workItem, CancellationToken stoppingToken)
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
            logger.LogWarning(
                exception,
                "GAC background worker {WorkerId} lookup failed for player {AllyCode}",
                workerId,
                key.AllyCode);
            result = CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "No se ha podido resolver el rival de Gran Arena. Inténtalo de nuevo en un minuto.");
        }
        finally
        {
            stopwatch.Stop();
        }

        gacTelemetry.RecordOpponentLookup(stopwatch.Elapsed, result.Status, cacheHit: false, "provider");
        logger.LogInformation(
            "GAC background worker {WorkerId} completed provider lookup for {AllyCode} with {Status} in {ElapsedMs} ms",
            workerId,
            key.AllyCode,
            result.Status,
            stopwatch.Elapsed.TotalMilliseconds);

        lock (gate)
        {
            long currentGeneration = GetGeneration(key.AllyCode);
            if (currentGeneration != workItem.Generation)
            {
                logger.LogDebug(
                    "Discarding stale GAC background result for {AllyCode}: generation {CompletedGeneration}, current {CurrentGeneration}",
                    key.AllyCode,
                    workItem.Generation,
                    currentGeneration);
                return;
            }

            entries[key] = new Entry(
                result,
                DateTimeOffset.UtcNow.AddMinutes(result.Status == CurrentGacOpponentStatus.Found ? 5 : 1));
        }
    }

    private long GetGeneration(long allyCode)
    {
        if (generations.TryGetValue(allyCode, out long generation))
        {
            return generation;
        }

        generation = ++generationSequence;
        generations[allyCode] = generation;
        return generation;
    }

    private sealed record Entry(CurrentGacOpponentLookup Result, DateTimeOffset ExpiresAt);
    private readonly record struct LookupWorkItem((long AllyCode, GacFormat? Format) Key, long Generation);
}
