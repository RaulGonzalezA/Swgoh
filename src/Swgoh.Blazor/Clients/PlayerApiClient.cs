using System.Net;

namespace Swgoh.Blazor.Clients;

public sealed class PlayerApiClient(HttpClient httpClient)
{
    public async Task<PlayerViewModel?> GetAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync($"/api/v1/players/{allyCode}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlayerViewModel>(cancellationToken);
    }

    public sealed record PlayerViewModel(long AllyCode, string Name, long GalacticPower, DateTimeOffset UpdatedAtUtc);
}
