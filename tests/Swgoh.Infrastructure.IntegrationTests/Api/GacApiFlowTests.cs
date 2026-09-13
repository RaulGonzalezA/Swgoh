using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Swgoh.Infrastructure.IntegrationTests.Persistence;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Api;

[Collection(MongoDbTestGroup.Name)]
public sealed class GacApiFlowTests(MongoDbContainerFixture fixture)
{
    [Fact]
    public async Task DefenseRequirements_AndLeagueTransition_AreExposedOverHttp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var connectionStringScope = new EnvironmentVariableScope(
            "ConnectionStrings__swgoh",
            fixture.ConnectionString);
        await using var factory = new SwgohApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage requirementsResponse = await client.GetAsync(
            "/api/v1/gac/defense-requirements?league=Kyber&format=5v5",
            cancellationToken);
        using JsonDocument requirements = await ReadJsonAsync(requirementsResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, requirementsResponse.StatusCode);
        Assert.Equal("Kyber", requirements.RootElement.GetProperty("league").GetString());
        Assert.Equal("5v5", requirements.RootElement.GetProperty("format").GetString());
        Assert.Equal(11, requirements.RootElement.GetProperty("squadDefenseCount").GetInt32());
        Assert.Equal(3, requirements.RootElement.GetProperty("fleetDefenseCount").GetInt32());

        using HttpResponseMessage transitionResponse = await client.GetAsync(
            "/api/v1/gac/defense-requirements/transition?fromLeague=Aurodium&toLeague=Kyber&format=3v3",
            cancellationToken);
        using JsonDocument transition = await ReadJsonAsync(transitionResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, transitionResponse.StatusCode);
        Assert.Equal("Aurodium", transition.RootElement.GetProperty("fromLeague").GetString());
        Assert.Equal("Kyber", transition.RootElement.GetProperty("toLeague").GetString());
        Assert.Equal("3v3", transition.RootElement.GetProperty("format").GetString());
        Assert.Equal(13, transition.RootElement.GetProperty("fromSquadDefenseCount").GetInt32());
        Assert.Equal(15, transition.RootElement.GetProperty("toSquadDefenseCount").GetInt32());
        Assert.Equal(2, transition.RootElement.GetProperty("squadDefenseDelta").GetInt32());
        Assert.Equal(1, transition.RootElement.GetProperty("fleetDefenseDelta").GetInt32());
        Assert.Equal(2, transition.RootElement.GetProperty("additionalSquadDefenses").GetInt32());
        Assert.Equal(1, transition.RootElement.GetProperty("additionalFleetDefenses").GetInt32());
        Assert.True(transition.RootElement.GetProperty("isPromotion").GetBoolean());
    }

    [Fact]
    public async Task DefenseRequirements_WithUnknownLeague_ReturnsValidationProblem()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var connectionStringScope = new EnvironmentVariableScope(
            "ConnectionStrings__swgoh",
            fixture.ConnectionString);
        await using var factory = new SwgohApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/api/v1/gac/defense-requirements?league=Beskar&format=5v5",
            cancellationToken);
        using JsonDocument problem = await ReadJsonAsync(response, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("gac", out JsonElement errors));
        Assert.Contains("League must be", errors[0].GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private sealed class SwgohApiFactory : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
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
