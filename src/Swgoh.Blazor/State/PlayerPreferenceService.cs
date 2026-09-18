using System.Text.Json;
using Microsoft.JSInterop;

namespace Swgoh.Blazor.State;

public sealed class PlayerPreferenceService(IJSRuntime jsRuntime)
{
    public async ValueTask<long?> GetAllyCodeAsync(CancellationToken cancellationToken = default)
    {
        string? stored = await jsRuntime.InvokeAsync<string?>(
            "swgohPreferences.getAllyCode",
            cancellationToken);
        return long.TryParse(stored, out long allyCode) ? allyCode : null;
    }

    public ValueTask SetAllyCodeAsync(long allyCode, CancellationToken cancellationToken = default) =>
        jsRuntime.InvokeVoidAsync(
            "swgohPreferences.setAllyCode",
            cancellationToken,
            allyCode.ToString());

    public async ValueTask<int?> GetDailyCrystalBudgetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        string? stored = await jsRuntime.InvokeAsync<string?>(
            "swgohPreferences.getDailyCrystalBudget",
            cancellationToken,
            allyCode.ToString());
        return int.TryParse(stored, out int budget)
            ? Math.Clamp(budget, 0, 5_000)
            : null;
    }

    public ValueTask SetDailyCrystalBudgetAsync(
        long allyCode,
        int dailyCrystalBudget,
        CancellationToken cancellationToken = default) =>
        jsRuntime.InvokeVoidAsync(
            "swgohPreferences.setDailyCrystalBudget",
            cancellationToken,
            allyCode.ToString(),
            Math.Clamp(dailyCrystalBudget, 0, 5_000).ToString());

    public async ValueTask<IReadOnlyDictionary<string, decimal>> GetDailyResourceCadencesAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        string? stored = await jsRuntime.InvokeAsync<string?>(
            "swgohPreferences.getDailyResourceCadences",
            cancellationToken,
            allyCode.ToString());
        if (string.IsNullOrWhiteSpace(stored))
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal);
        }

        Dictionary<string, decimal>? cadences = JsonSerializer.Deserialize<Dictionary<string, decimal>>(stored);
        return cadences?
            .Where(pair => pair.Value > 0m)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, decimal>(StringComparer.Ordinal);
    }

    public ValueTask SetDailyResourceCadencesAsync(
        long allyCode,
        IReadOnlyDictionary<string, decimal> cadences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cadences);

        Dictionary<string, decimal> normalized = cadences
            .Where(pair => pair.Value > 0m)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        string json = JsonSerializer.Serialize(normalized);
        return jsRuntime.InvokeVoidAsync(
            "swgohPreferences.setDailyResourceCadences",
            cancellationToken,
            allyCode.ToString(),
            json);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) =>
        jsRuntime.InvokeVoidAsync("swgohPreferences.clearAllyCode", cancellationToken);
}
