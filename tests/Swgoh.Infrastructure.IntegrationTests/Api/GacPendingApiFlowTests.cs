using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.IntegrationTests.Persistence;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Api;

[Collection(MongoDbTestGroup.Name)]
public sealed class GacPendingApiFlowTests(MongoDbContainerFixture fixture)
{
    [Fact]
    public async Task PlannerCurrent_WhenOpponentLookupIsPending_ReturnsAcceptedWithoutWaitingForProvider()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var connection = new EnvironmentVariableScope("ConnectionStrings__swgoh", fixture.ConnectionString);
        await using var factory = new PendingLookupApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/api/v1/gac/players/476825771/planner/current", cancellationToken);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Pending", body.RootElement.GetProperty("status").GetString());
        Assert.Contains("segundo plano", body.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PendingLookupApiFactory : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICurrentGacOpponentSource>();
                services.AddSingleton<ICurrentGacOpponentSource, PendingLookupSource>();
            });
        }
    }

    private sealed class PendingLookupSource : ICurrentGacOpponentSource
    {
        public Task<CurrentGacOpponentLookup> GetAsync(
            long allyCode,
            GacFormat? formatOverride,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.Pending,
                "Buscando rival de Gran Arena en segundo plano…"));
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string name;
        private readonly string? previousValue;

        public EnvironmentVariableScope(string name, string value)
        {
            this.name = name;
            previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(name, previousValue);
    }
}
