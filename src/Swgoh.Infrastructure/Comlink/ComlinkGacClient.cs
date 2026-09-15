using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Swgoh.Infrastructure.Comlink;

internal interface IComlinkGacClient
{
    Task<JsonDocument> GetPlayerByAllyCodeAsync(long allyCode, CancellationToken cancellationToken);
    Task<JsonDocument> GetPlayerByIdAsync(string playerId, CancellationToken cancellationToken);
    Task<JsonDocument> GetEventsAsync(CancellationToken cancellationToken);
    Task<ComlinkGacResponse> GetLeaderboardAsync(
        string eventInstanceId,
        string bracketId,
        CancellationToken cancellationToken);
}

internal sealed record ComlinkGacResponse(HttpStatusCode StatusCode, string Body)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

internal sealed class ComlinkGacClient(IHttpClientFactory httpClientFactory) : IComlinkGacClient
{
    internal const string HttpClientName = "SwgohComlinkGac";

    public Task<JsonDocument> GetPlayerByAllyCodeAsync(long allyCode, CancellationToken cancellationToken) =>
        PostJsonAsync(
            "playerArena",
            new
            {
                payload = new { allyCode = allyCode.ToString(), playerDetailsOnly = true },
                enums = false
            },
            cancellationToken);

    public Task<JsonDocument> GetPlayerByIdAsync(string playerId, CancellationToken cancellationToken) =>
        PostJsonAsync(
            "playerArena",
            new
            {
                payload = new { playerId, playerDetailsOnly = true },
                enums = false
            },
            cancellationToken);

    public Task<JsonDocument> GetEventsAsync(CancellationToken cancellationToken) =>
        PostJsonAsync("getEvents", new { payload = new { }, enums = false }, cancellationToken);

    public async Task<ComlinkGacResponse> GetLeaderboardAsync(
        string eventInstanceId,
        string bracketId,
        CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "getLeaderboard",
            new
            {
                payload = new
                {
                    leaderboardType = 4,
                    eventInstanceId,
                    groupId = bracketId
                },
                enums = false
            },
            cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new ComlinkGacResponse(response.StatusCode, body);
    }

    private async Task<JsonDocument> PostJsonAsync(
        string path,
        object request,
        CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
