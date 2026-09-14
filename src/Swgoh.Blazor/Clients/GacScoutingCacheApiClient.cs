namespace Swgoh.Blazor.Clients;

public sealed class GacScoutingCacheApiClient(HttpClient httpClient)
{
    public async Task RefreshAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/api/v1/gac/players/{allyCode}/current-opponent/scouting/refresh",
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
