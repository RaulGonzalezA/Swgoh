using System.Net;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Infrastructure.GameData;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.GameData;

public sealed class SwgohGameDataCatalogClientTests
{
    [Fact]
    public async Task GetAsync_ParsesSpecialSkillTiersAndCachesCatalog()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var handler = new GameDataHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://game-data.test/")
        };
        var client = new SwgohGameDataCatalogClient(new StubHttpClientFactory(httpClient));

        GameDataCatalog first = await client.GetAsync(cancellationToken);
        GameDataCatalog second = await client.GetAsync(cancellationToken);

        Assert.Same(first, second);
        Assert.Equal(4, first.Skills["skill-zeta"].ZetaTier);
        Assert.Null(first.Skills["skill-zeta"].OmicronTier);
        Assert.Equal(3, first.Skills["skill-omicron"].OmicronTier);
        Assert.Null(first.Skills["skill-omicron"].ZetaTier);
        Assert.Equal(3, first.Skills["skill-zeta-omicron"].ZetaTier);
        Assert.Equal(4, first.Skills["skill-zeta-omicron"].OmicronTier);
        Assert.False(first.Units["CHARACTER"].IsShip);
        Assert.True(first.Units["SHIP"].IsShip);
        Assert.Equal(4, handler.RequestCount);
    }

    private sealed class StubHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(SwgohGameDataCatalogClient.HttpClientName, name);
            return httpClient;
        }
    }

    private sealed class GameDataHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            string json = request.RequestUri?.AbsolutePath switch
            {
                "/units_gas.json" => """
                    {"data":[
                      {"baseId":"CHARACTER","combatType":1},
                      {"baseId":"SHIP","combatType":2}
                    ]}
                    """,
                "/skill.json" => """
                    {"data":[
                      {"id":"skill-zeta","tier":[
                        {"isZetaTier":false,"isOmicronTier":false},
                        {"isZetaTier":false,"isOmicronTier":false},
                        {"isZetaTier":true,"isOmicronTier":false}
                      ]},
                      {"id":"skill-omicron","tier":[
                        {"isZetaTier":false,"isOmicronTier":false},
                        {"isZetaTier":false,"isOmicronTier":true}
                      ]},
                      {"id":"skill-zeta-omicron","tier":[
                        {"isZetaTier":false,"isOmicronTier":false},
                        {"isZetaTier":true,"isOmicronTier":false},
                        {"isZetaTier":false,"isOmicronTier":true}
                      ]}
                    ]}
                    """,
                "/unitGuideDefinition.json" => "{\"data\":[]}",
                "/requirement.json" => "{\"data\":[]}",
                _ => throw new InvalidOperationException($"Unexpected Game Data URL: {request.RequestUri}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
