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

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) =>
        jsRuntime.InvokeVoidAsync("swgohPreferences.clearAllyCode", cancellationToken);
}
