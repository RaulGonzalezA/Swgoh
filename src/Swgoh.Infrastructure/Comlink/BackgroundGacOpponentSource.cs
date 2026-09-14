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
    private readonly Channel<(long AllyCode, GacFormat? Format)> queue = Channel.CreateBounded<(long, GacFormat?)>(32);

    public Task<CurrentGacOpponentLookup> GetAsync(long allyCode, GacFormat? formatOverride, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode));
        }

        lock (gate)
        {
            foreach (var expired in entries.Where(item => item.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(item => item.Key).ToArray())
            {
                entries.Remove(expired);
            }

            var key = (allyCode, formatOverride);
            if (entries.TryGetValue(key, out Entry? existing))
            {
                return Task.FromResult(existing.Result);
            }

            var pending = CurrentGacOpponentLookup.Unavailable(CurrentGacOpponentStatus.Pending, "Buscando rival de Gran Arena en segundo plano…");
            if (!queue.Writer.TryWrite(key))
            {
                return Task.FromResult(CurrentGacOpponentLookup.Unavailable(
                    CurrentGacOpponentStatus.OpponentUnavailable, "Hay demasiadas búsquedas pendientes. Inténtalo más tarde."));
            }

            entries[key] = new Entry(pending, DateTimeOffset.MaxValue);
            return Task.FromResult(pending);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var key in queue.Reader.ReadAllAsync(stoppingToken))
        {
            CurrentGacOpponentLookup result;
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
                result = CurrentGacOpponentLookup.Unavailable(CurrentGacOpponentStatus.OpponentUnavailable,
                    "No se ha podido resolver el rival de Gran Arena. Inténtalo de nuevo en un minuto.");
            }

            lock (gate)
            {
                entries[key] = new Entry(result, DateTimeOffset.UtcNow.AddMinutes(result.Status == CurrentGacOpponentStatus.Found ? 5 : 1));
            }
        }
    }

    private sealed record Entry(CurrentGacOpponentLookup Result, DateTimeOffset ExpiresAt);
}
