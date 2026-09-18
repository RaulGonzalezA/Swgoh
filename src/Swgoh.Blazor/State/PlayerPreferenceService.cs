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

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) =>
        jsRuntime.InvokeVoidAsync("swgohPreferences.clearAllyCode", cancellationToken);
}
