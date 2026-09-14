extern alias BlazorApp;

using System.Net;
using System.Net.Http.Json;

using BlazorApp::Swgoh.Blazor.Clients;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Blazor;

public sealed class GacDeferredResponseTests
{
    [Fact]
    public async Task PendingResponseIsPolledUntilLookupCompletes()
    {
        using var handler = new PendingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var client = new GacPlannerApiClient(http);

        var result = await client.GetCurrentAsync(476825771, TestContext.Current.CancellationToken);

        Assert.Equal("Completed", result.Message);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ScoutingClientTreatsAcceptedPendingAsDeferredResponse()
    {
        using var handler = new AcceptedPendingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var client = new GacApiClient(http);

        GacApiClient.CurrentGacResult result = await client.GetCurrentOpponentAsync(
            476825771,
            TestContext.Current.CancellationToken);

        Assert.Null(result.Scouting);
        Assert.Equal("Búsqueda completada sin rival.", result.Message);
        Assert.Equal(2, handler.Calls);
    }

    private sealed class PendingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new { Status = Calls == 1 ? "Pending" : "OpponentUnavailable", Message = "Completed" })
            });
        }
    }

    private sealed class AcceptedPendingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Calls == 1
                ? new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = JsonContent.Create(new { Status = "Pending", Message = "Buscando rival." })
                }
                : new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { Status = "OpponentUnavailable", Message = "Búsqueda completada sin rival." })
                });
        }
    }

    [Fact]
    public async Task DeferredResponseCompletesWhenDataArrives()
    {
        using var handler = new DeferredHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var client = new GacPlannerApiClient(http);

        Task<GacPlannerApiClient.PlannerResult> pending = client.GetCurrentAsync(476825771, TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { Status = "OpponentUnavailable", Message = "Búsqueda completada sin rival." })
        });

        GacPlannerApiClient.PlannerResult result = await pending;
        Assert.Equal("Búsqueda completada sin rival.", result.Message);
    }

    [Fact]
    public async Task LeavingPageCancelsPendingRequest()
    {
        using var handler = new DeferredHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        using var page = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = new GacPlannerApiClient(http);

        Task<GacPlannerApiClient.PlannerResult> pending = client.GetCurrentAsync(476825771, page.Token);
        page.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private sealed class DeferredHandler : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Response.Task.WaitAsync(cancellationToken);
    }
}
