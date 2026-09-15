using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class GacHistoryApiClient(HttpClient httpClient)
{
    public async Task<HistorySyncViewModel> SyncAsync(
        long opponentAllyCode,
        string format,
        int maxRounds = 60,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(opponentAllyCode, 100_000_000L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opponentAllyCode, 999_999_999L);
        string normalizedFormat = format.Equals("3v3", StringComparison.OrdinalIgnoreCase) ? "3v3" : "5v5";
        int normalizedRounds = Math.Clamp(maxRounds, 1, 200);

        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/api/v1/gac/opponents/{opponentAllyCode}/history/sync?format={normalizedFormat}&maxRounds={normalizedRounds}",
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HistorySyncViewModel>(cancellationToken)
            ?? throw new HttpRequestException("La API devolvió una sincronización histórica vacía.");
    }

    public sealed record HistorySyncViewModel(
        int ProvidersAttempted,
        int ProvidersSucceeded,
        int RoundsReceived,
        int RoundsImported,
        IReadOnlyCollection<string> Sources,
        IReadOnlyCollection<string> Warnings)
    {
        public bool HasConfiguredProvider => ProvidersAttempted > 0;
        public bool ImportedAnyRound => RoundsImported > 0;
    }
}
