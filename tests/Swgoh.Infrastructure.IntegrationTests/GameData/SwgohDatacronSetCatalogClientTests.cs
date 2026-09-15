using System.Text.Json;

using Swgoh.Application.GameData;
using Swgoh.Infrastructure.GameData;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.GameData;

public sealed class SwgohDatacronSetCatalogClientTests
{
    [Fact]
    public void Parse_ReadsExpirationTimeFromStringAndNumber()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "data": [
                { "id": 34, "expirationTimeMs": "1795680000000" },
                { "id": "33", "expirationTimeMs": 1793257200000 },
                { "id": 32 }
              ]
            }
            """);

        IReadOnlyDictionary<string, DatacronSetExpiration> result =
            SwgohDatacronSetCatalogClient.Parse(document.RootElement);

        Assert.Equal(3, result.Count);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1795680000000), result["34"].ExpiresAtUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793257200000), result["33"].ExpiresAtUtc);
        Assert.Null(result["32"].ExpiresAtUtc);
    }
}
