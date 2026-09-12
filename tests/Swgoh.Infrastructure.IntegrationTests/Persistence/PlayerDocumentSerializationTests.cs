using MongoDB.Bson;
using MongoDB.Bson.Serialization;

using Swgoh.Infrastructure.Persistence.Documents;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Persistence;

public sealed class PlayerDocumentSerializationTests
{
    [Fact]
    public void PlayerDocument_RoundTrip_MapsIdToMongoId()
    {
        var document = new PlayerDocument
        {
            Id = 123456789,
            AllyCode = 123456789,
            PlayerId = "player-id",
            Name = "Player",
            Level = 85,
            GalacticPower = 12_345_678,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        BsonDocument bson = document.ToBsonDocument();

        Assert.Equal(document.Id, bson["_id"].AsInt64);
        Assert.False(bson.Contains("Id"));

        PlayerDocument roundTripped = BsonSerializer.Deserialize<PlayerDocument>(bson);

        Assert.Equal(document.Id, roundTripped.Id);
        Assert.Equal(document.AllyCode, roundTripped.AllyCode);
        Assert.Equal(document.PlayerId, roundTripped.PlayerId);
    }
}
