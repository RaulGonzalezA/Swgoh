using System.Net;
using System.Text;
using System.Text.Json;

using Swgoh.Infrastructure.Comlink;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Comlink;

public sealed class SwgohStatsClientTests
{
    [Fact]
    public async Task CalculateRosterStatsAsync_WhenAllRosterUnitsAreReturned_MapsPowerAndFinalStats()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = CreateHttpClient(
            """
            [{
              "id":"unit-1",
              "gp":"42000",
              "stats":{"final":{"1":120000,"28":90000,"5":327,"6":10500,"7":8200,"8":62.5,"9":51.2,"17":83.4,"18":96.1,"16":192}}
            }]
            """);
        var client = new SwgohStatsClient(httpClient);

        IReadOnlyDictionary<string, CalculatedRosterUnitStats> result = await client.CalculateRosterStatsAsync(
            CreateRoster("unit-1"),
            cancellationToken);

        CalculatedRosterUnitStats unit = result["unit-1"];
        Assert.Equal(42_000, unit.GalacticPower);
        Assert.NotNull(unit.Stats);
        Assert.Equal(327m, unit.Stats.Speed);
        Assert.Equal(120_000m, unit.Stats.Health);
        Assert.Equal(90_000m, unit.Stats.Protection);
        Assert.Equal(83.4m, unit.Stats.Potency);
    }

    [Fact]
    public async Task CalculateRosterStatsAsync_WhenStatsAreMissing_StillMapsPower()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = CreateHttpClient("""[{"id":"unit-1","gp":42000}]""");
        var client = new SwgohStatsClient(httpClient);

        IReadOnlyDictionary<string, CalculatedRosterUnitStats> result = await client.CalculateRosterStatsAsync(
            CreateRoster("unit-1"),
            cancellationToken);

        Assert.Equal(42_000, result["unit-1"].GalacticPower);
        Assert.Null(result["unit-1"].Stats);
    }

    [Fact]
    public async Task CalculateRosterStatsAsync_WhenResponseOmitsRosterUnit_Throws()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = CreateHttpClient("[]");
        var client = new SwgohStatsClient(httpClient);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CalculateRosterStatsAsync(CreateRoster("unit-1"), cancellationToken));

        Assert.Contains("did not return Galactic Power", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"invalid\"")]
    [InlineData("-1")]
    public async Task CalculateRosterStatsAsync_WhenPowerIsInvalid_Throws(string galacticPowerJson)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string response = $"[{{\"id\":\"unit-1\",\"gp\":{galacticPowerJson}}}]";
        using var httpClient = CreateHttpClient(response);
        var client = new SwgohStatsClient(httpClient);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CalculateRosterStatsAsync(CreateRoster("unit-1"), cancellationToken));

        Assert.Contains("Galactic Power", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<JsonElement> CreateRoster(params string[] ids)
    {
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(ids.Select(id => new Dictionary<string, string> { ["id"] = id })));
        return [.. document.RootElement.EnumerateArray().Select(element => element.Clone())];
    }

    private static HttpClient CreateHttpClient(string json) => new(new JsonHandler(json))
    {
        BaseAddress = new Uri("http://stats.test/")
    };

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
