using System.IO.Compression;
using System.Net;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Infrastructure.GameData;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.GameData;

public sealed class SwgohGameDataCatalogClientTests
{
    [Fact]
    public async Task GetAsync_ParsesUnitMetadataSpecialSkillTiersAndCachesCatalog()
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

        GameUnitDefinition character = first.Units["CHARACTER"];
        Assert.False(character.IsShip);
        Assert.Equal("UNIT_CHARACTER_NAME", character.NameKey);
        Assert.Equal("Capitán clon", character.Name);
        Assert.Equal("tex.charui_character", character.ThumbnailName);
        Assert.Contains("República Galáctica", character.Factions);
        Assert.DoesNotContain("Prueba interna", character.Factions);
        Assert.Contains("affiliation_republic", character.Tags);
        Assert.Contains("affiliation_internal_test", character.Tags);
        Assert.Contains("role_support", character.Tags);

        Assert.True(first.Units["SHIP"].IsShip);
        Assert.Equal("Caza de la República", first.Units["SHIP"].Name);
        Assert.Equal(6, handler.RequestCount);
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
            string path = request.RequestUri?.AbsolutePath
                ?? throw new InvalidOperationException("Game Data request URI is required.");

            if (string.Equals(path, "/Loc_SPA_XM.txt.json.br", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = CreateBrotliContent("""
                        {
                          "UNIT_CHARACTER_NAME":"Capitán clon",
                          "UNIT_SHIP_NAME":"Caza de la República",
                          "CATEGORY_GALACTICREPUBLIC_DESC":"República Galáctica",
                          "CATEGORY_SUPPORT_DESC":"Apoyo",
                          "CATEGORY_INTERNAL_TEST_DESC":"Prueba interna"
                        }
                        """)
                });
            }

            string json = path switch
            {
                "/units_gas.json" => """
                    {"data":[
                      {
                        "baseId":"CHARACTER",
                        "combatType":1,
                        "nameKey":"UNIT_CHARACTER_NAME",
                        "thumbnailName":"tex.charui_character",
                        "categoryId":["affiliation_republic","affiliation_internal_test","role_support"]
                      },
                      {
                        "baseId":"SHIP",
                        "combatType":2,
                        "nameKey":"UNIT_SHIP_NAME",
                        "thumbnailName":"tex.charui_ship",
                        "categoryId":["affiliation_republic","shipclass_fighter"]
                      }
                    ]}
                    """,
                "/category.json" => """
                    {"data":[
                      {"id":"affiliation_republic","descKey":"CATEGORY_GALACTICREPUBLIC_DESC","visible":true},
                      {"id":"affiliation_internal_test","descKey":"CATEGORY_INTERNAL_TEST_DESC","visible":false},
                      {"id":"role_support","descKey":"CATEGORY_SUPPORT_DESC","visible":true},
                      {"id":"shipclass_fighter","descKey":"CATEGORY_FIGHTER_DESC","visible":false}
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

        private static HttpContent CreateBrotliContent(string json)
        {
            using var output = new MemoryStream();
            using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                brotli.Write(bytes);
            }

            return new ByteArrayContent(output.ToArray());
        }
    }
}
