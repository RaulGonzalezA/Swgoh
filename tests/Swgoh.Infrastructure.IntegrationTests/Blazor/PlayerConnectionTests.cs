extern alias BlazorApp;

using System.Net;
using System.Net.Http.Json;

using BlazorApp::Swgoh.Blazor.Clients;
using BlazorApp::Swgoh.Blazor.State;

using Microsoft.JSInterop;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Blazor;

public sealed class PlayerConnectionTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task ConnectImportsOnlyMissingPlayers(bool exists, int expectedImports)
    {
        using var handler = new PlayerHandler(exists);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var session = new PlayerSessionState();
        var context = new PlayerContextService(new PlayerApiClient(http), new PlayerPreferenceService(new Preferences()), session);

        Assert.True(await context.ConnectAsync(123456789, TestContext.Current.CancellationToken));

        Assert.Equal(expectedImports, handler.Imports);
        Assert.Equal(123456789L, session.AllyCode);
        Assert.Equal("Test player", session.PlayerName);
    }

    [Fact]
    public async Task RestoreDoesNotImportMissingPlayer()
    {
        using var handler = new PlayerHandler(false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var session = new PlayerSessionState();
        var context = new PlayerContextService(new PlayerApiClient(http), new PlayerPreferenceService(new Preferences()), session);

        Assert.False(await context.RestoreAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, handler.Imports);
        Assert.Null(session.AllyCode);
    }

    [Fact]
    public async Task FailedImportPreservesExistingSessionAndStatusCode()
    {
        using var handler = new PlayerHandler(false, HttpStatusCode.BadGateway);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var session = new PlayerSessionState();
        session.SetPlayer(987654321, "Previous player");
        var context = new PlayerContextService(new PlayerApiClient(http), new PlayerPreferenceService(new Preferences()), session);

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => context.ConnectAsync(123456789, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Equal(987654321L, session.AllyCode);
    }

    private sealed class PlayerHandler(bool exists, HttpStatusCode importStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Imports { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/api/v1/players/123456789/refresh", request.RequestUri!.AbsolutePath);
                Imports++;
                exists = importStatus == HttpStatusCode.OK;
                return Task.FromResult(new HttpResponseMessage(importStatus));
            }

            return Task.FromResult(exists
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PlayerApiClient.PlayerViewModel(
                        123456789, "test", "Test player", null, 85, 1000,
                        DateTimeOffset.UnixEpoch, 1))
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class Preferences : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(identifier == "swgohPreferences.getAllyCode" ? (TValue)(object)"123456789" : default!);
    }
}
