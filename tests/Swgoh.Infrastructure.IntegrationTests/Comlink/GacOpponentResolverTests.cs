using System.Text.Json;

using Swgoh.Infrastructure.Comlink;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Comlink;

public sealed class GacOpponentResolverTests
{
    [Fact]
    public void Resolve_RoundTwo_UsesSwissScoreAndRankInsteadOfAdjacentBracketPosition()
    {
        const long allyCode = 476825771;
        GacBracketParticipant[] participants =
        [
            Participant("self", allyCode, "Self", score: 1, rank: 1),
            Participant("adjacent-wrong", 111111111, "Adjacent", score: 0, rank: 8),
            Participant("same-2", 222222222, "Same 2", score: 1, rank: 2),
            Participant("loss-2", 333333333, "Loss 2", score: 0, rank: 7),
            Participant("same-3", 444444444, "Same 3", score: 1, rank: 3),
            Participant("loss-3", 555555555, "Loss 3", score: 0, rank: 6),
            Participant("actual-opponent", 987654321, "Actual", score: 1, rank: 4),
            Participant("loss-4", 666666666, "Loss 4", score: 0, rank: 5)
        ];

        GacBracketParticipant? opponent = GacOpponentResolver.Resolve(
            participants,
            playerIndex: 0,
            playerId: "self",
            allyCode,
            roundNumber: 2,
            methodPrefix: string.Empty,
            out string method);

        Assert.NotNull(opponent);
        Assert.Equal(987654321, opponent.AllyCode);
        Assert.Equal("PvpScoreRankPairing", method);
    }

    [Fact]
    public void Resolve_RoundTwoWithoutSwissData_DoesNotGuessAdjacentOpponent()
    {
        const long allyCode = 476825771;
        GacBracketParticipant[] participants =
        [
            Participant("self", allyCode, "Self"),
            Participant("adjacent", 111111111, "Adjacent"),
            Participant("p2", 222222222, "P2"),
            Participant("p3", 333333333, "P3"),
            Participant("p4", 444444444, "P4"),
            Participant("p5", 555555555, "P5"),
            Participant("p6", 666666666, "P6"),
            Participant("p7", 777777777, "P7")
        ];

        GacBracketParticipant? opponent = GacOpponentResolver.Resolve(
            participants,
            playerIndex: 0,
            playerId: "self",
            allyCode,
            roundNumber: 2,
            methodPrefix: string.Empty,
            out string method);

        Assert.Null(opponent);
        Assert.Equal("Unavailable", method);
    }

    [Fact]
    public void Resolve_RoundOneWithoutSwissData_UsesInitialBracketOrder()
    {
        const long allyCode = 476825771;
        GacBracketParticipant[] participants =
        [
            Participant("self", allyCode, "Self"),
            Participant("initial-opponent", 987654321, "Initial"),
            Participant("p2", 222222222, "P2"),
            Participant("p3", 333333333, "P3"),
            Participant("p4", 444444444, "P4"),
            Participant("p5", 555555555, "P5"),
            Participant("p6", 666666666, "P6"),
            Participant("p7", 777777777, "P7")
        ];

        GacBracketParticipant? opponent = GacOpponentResolver.Resolve(
            participants,
            playerIndex: 0,
            playerId: "self",
            allyCode,
            roundNumber: 1,
            methodPrefix: "PersistedBracket",
            out string method);

        Assert.NotNull(opponent);
        Assert.Equal(987654321, opponent.AllyCode);
        Assert.Equal("PersistedBracketBracketOrderPairing", method);
    }

    private static GacBracketParticipant Participant(
        string playerId,
        long allyCode,
        string name,
        int? score = null,
        int? rank = null)
    {
        object payload = score is null || rank is null
            ? new { id = playerId, allyCode = allyCode.ToString(), name }
            : new
            {
                id = playerId,
                allyCode = allyCode.ToString(),
                name,
                pvpStatus = new { score, rank }
            };
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return new GacBracketParticipant(allyCode, name, playerId, document.RootElement.Clone());
    }
}
