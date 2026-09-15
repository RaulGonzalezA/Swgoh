namespace Swgoh.Infrastructure.Comlink;

internal static class GacOpponentResolver
{
    private const int BracketSize = 8;

    public static GacBracketParticipant? Resolve(
        IReadOnlyList<GacBracketParticipant> participants,
        int playerIndex,
        string playerId,
        long allyCode,
        int? roundNumber,
        string methodPrefix,
        out string resolutionMethod)
    {
        if (playerIndex < 0 || playerIndex >= participants.Count)
        {
            resolutionMethod = AddPrefix(methodPrefix, "Unavailable");
            return null;
        }

        GacBracketParticipant current = participants[playerIndex];
        if (GacBracketParser.TryReadPvpInt(current.Element, "score", out int currentScore))
        {
            GacBracketParticipant[] sameRecord =
            [
                .. participants.Where(participant =>
                    GacBracketParser.TryReadPvpInt(participant.Element, "score", out int score) &&
                    score == currentScore)
            ];

            if (sameRecord.Length is 2 or 4)
            {
                var ranked = new List<(GacBracketParticipant Participant, int Rank)>(sameRecord.Length);
                foreach (GacBracketParticipant participant in sameRecord)
                {
                    if (!GacBracketParser.TryReadPvpInt(participant.Element, "rank", out int rank))
                    {
                        ranked.Clear();
                        break;
                    }

                    ranked.Add((participant, rank));
                }

                if (ranked.Count == sameRecord.Length &&
                    ranked.Select(value => value.Rank).Distinct().Count() == ranked.Count)
                {
                    var ordered = ranked.OrderBy(value => value.Rank).ToArray();
                    int currentIndex = Array.FindIndex(ordered, value =>
                        string.Equals(value.Participant.PlayerId, playerId, StringComparison.Ordinal) ||
                        value.Participant.AllyCode == allyCode);
                    if (currentIndex >= 0)
                    {
                        int pairedIndex = ordered.Length - 1 - currentIndex;
                        if (pairedIndex >= 0 && pairedIndex < ordered.Length && pairedIndex != currentIndex)
                        {
                            resolutionMethod = AddPrefix(methodPrefix, "PvpScoreRankPairing");
                            return ordered[pairedIndex].Participant;
                        }
                    }
                }
            }
        }

        // Bracket order is authoritative only for the opening round. In later rounds
        // adjacent positions can be unrelated to the current Swiss pairing.
        if (roundNumber == 1 && participants.Count == BracketSize)
        {
            int pairedIndex = playerIndex % 2 == 0 ? playerIndex + 1 : playerIndex - 1;
            if (pairedIndex >= 0 && pairedIndex < participants.Count)
            {
                resolutionMethod = AddPrefix(methodPrefix, "BracketOrderPairing");
                return participants[pairedIndex];
            }
        }

        resolutionMethod = AddPrefix(methodPrefix, "Unavailable");
        return null;
    }

    public static int? InferRoundNumber(IReadOnlyCollection<GacBracketParticipant> participants)
    {
        int[] scores =
        [
            .. participants
                .Select(participant =>
                    GacBracketParser.TryReadPvpInt(participant.Element, "score", out int score) ? score : -1)
                .Where(score => score >= 0)
        ];

        return scores.Length == 0 ? null : Math.Clamp(scores.Max() + 1, 1, 3);
    }

    private static string AddPrefix(string prefix, string method)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return method;
        }

        return prefix.EndsWith("Bracket", StringComparison.Ordinal) &&
               method.StartsWith("Bracket", StringComparison.Ordinal)
            ? prefix + method["Bracket".Length..]
            : prefix + method;
    }
}
