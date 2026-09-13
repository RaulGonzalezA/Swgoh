using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacHistoryProvider
{
    string Name { get; }
    bool IsEnabled { get; }

    Task<IReadOnlyCollection<GacHistoryRoundInput>> GetAsync(
        long allyCode,
        GacFormat format,
        int maxRounds,
        CancellationToken cancellationToken = default);
}

public sealed record GacHistorySyncResult(
    int ProvidersAttempted,
    int ProvidersSucceeded,
    int RoundsReceived,
    int RoundsImported,
    IReadOnlyCollection<string> Sources,
    IReadOnlyCollection<string> Warnings);

public interface IGacHistorySyncService
{
    Task<GacHistorySyncResult> SyncAsync(
        long allyCode,
        GacFormat format,
        int maxRounds,
        CancellationToken cancellationToken = default);
}

internal sealed class GacHistorySyncService(
    IEnumerable<IGacHistoryProvider> providers,
    IGacHistoryService historyService) : IGacHistorySyncService
{
    private const int MaxRounds = 200;

    public async Task<GacHistorySyncResult> SyncAsync(
        long allyCode,
        GacFormat format,
        int maxRounds,
        CancellationToken cancellationToken = default)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.");
        }

        int requestedRounds = Math.Clamp(maxRounds, 1, MaxRounds);
        IGacHistoryProvider[] enabledProviders = [.. providers.Where(provider => provider.IsEnabled)];
        if (enabledProviders.Length == 0)
        {
            return new GacHistorySyncResult(0, 0, 0, 0, [], []);
        }

        int succeeded = 0;
        int received = 0;
        int imported = 0;
        var sources = new List<string>();
        var warnings = new List<string>();

        foreach (IGacHistoryProvider provider in enabledProviders)
        {
            try
            {
                IReadOnlyCollection<GacHistoryRoundInput> rounds = await provider
                    .GetAsync(allyCode, format, requestedRounds, cancellationToken)
                    .ConfigureAwait(false);
                succeeded++;
                received += rounds.Count;
                if (rounds.Count == 0)
                {
                    continue;
                }

                GacHistoryImportResult import = await historyService
                    .ImportAsync(allyCode, rounds, cancellationToken)
                    .ConfigureAwait(false);
                imported += import.ImportedRounds;
                sources.Add(provider.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or ArgumentException)
            {
                warnings.Add($"{provider.Name}: {exception.Message}");
            }
        }

        return new GacHistorySyncResult(
            enabledProviders.Length,
            succeeded,
            received,
            imported,
            sources,
            warnings);
    }
}
