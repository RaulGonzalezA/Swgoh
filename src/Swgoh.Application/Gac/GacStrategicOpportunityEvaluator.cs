namespace Swgoh.Application.Gac;

internal static class GacStrategicOpportunityEvaluator
{
    private const decimal ViableScore = 25m;
    private const decimal ReplaceabilityWindow = 20m;
    private const decimal MaxLossPerDefense = 12m;
    private const decimal MaxOpportunityCost = 15m;
    private const int MaxFutureLossesCounted = 2;

    public static IReadOnlyDictionary<GacStrategicCandidateKey, GacOpportunityAssessment> Evaluate(
        IReadOnlyCollection<GacStrategicCandidateSnapshot> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        Dictionary<Guid, GacStrategicCandidateSnapshot[]> byDefense = candidates
            .GroupBy(candidate => candidate.DefenseId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var result = new Dictionary<GacStrategicCandidateKey, GacOpportunityAssessment>();

        foreach (GacStrategicCandidateSnapshot candidate in candidates)
        {
            result[candidate.Key] = EvaluateCandidate(candidate, byDefense);
        }

        return result;
    }

    private static GacOpportunityAssessment EvaluateCandidate(
        GacStrategicCandidateSnapshot candidate,
        IReadOnlyDictionary<Guid, GacStrategicCandidateSnapshot[]> byDefense)
    {
        HashSet<string> consumedUnits = candidate.UnitDefinitionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        GacStrategicCandidateSnapshot[] alternativesHere = byDefense[candidate.DefenseId]
            .Where(other =>
                other.TeamPresetId != candidate.TeamPresetId &&
                other.ScoreBeforeOpportunity >= ViableScore &&
                !Overlaps(consumedUnits, other.UnitDefinitionIds))
            .OrderByDescending(other => other.ScoreBeforeOpportunity)
            .ToArray();

        decimal replaceability = CalculateReplaceability(candidate, alternativesHere.FirstOrDefault());
        var futureLosses = new List<FutureLoss>();

        foreach ((Guid defenseId, GacStrategicCandidateSnapshot[] defenseCandidates) in byDefense)
        {
            if (defenseId == candidate.DefenseId)
            {
                continue;
            }

            GacStrategicCandidateSnapshot? best = defenseCandidates
                .Where(other => other.ScoreBeforeOpportunity >= ViableScore)
                .OrderByDescending(other => other.ScoreBeforeOpportunity)
                .FirstOrDefault();
            if (best is null || !Overlaps(consumedUnits, best.UnitDefinitionIds))
            {
                continue;
            }

            GacStrategicCandidateSnapshot? bestWithoutConsumedUnits = defenseCandidates
                .Where(other =>
                    other.ScoreBeforeOpportunity >= ViableScore &&
                    !Overlaps(consumedUnits, other.UnitDefinitionIds))
                .OrderByDescending(other => other.ScoreBeforeOpportunity)
                .FirstOrDefault();

            decimal loss = bestWithoutConsumedUnits is null
                ? CoverageLoss(best.ScoreBeforeOpportunity)
                : Math.Clamp(
                    best.ScoreBeforeOpportunity - bestWithoutConsumedUnits.ScoreBeforeOpportunity,
                    0m,
                    MaxLossPerDefense);
            if (loss > 0m)
            {
                futureLosses.Add(new FutureLoss(defenseId, loss, bestWithoutConsumedUnits is null));
            }
        }

        decimal rawFutureLoss = futureLosses
            .OrderByDescending(loss => loss.Value)
            .Take(MaxFutureLossesCounted)
            .Sum(loss => loss.Value);
        decimal opportunityCost = Math.Round(
            Math.Clamp(rawFutureLoss * replaceability, 0m, MaxOpportunityCost),
            1);
        int futureDefensesAtRisk = futureLosses.Count(loss => loss.Value >= 3m || loss.LosesCoverage);

        return new GacOpportunityAssessment(
            opportunityCost,
            alternativesHere.Length,
            futureDefensesAtRisk,
            BuildSummary(opportunityCost, alternativesHere.Length, futureDefensesAtRisk));
    }

    private static decimal CalculateReplaceability(
        GacStrategicCandidateSnapshot candidate,
        GacStrategicCandidateSnapshot? bestAlternative)
    {
        if (bestAlternative is null)
        {
            return 0m;
        }

        decimal gap = Math.Max(0m, candidate.ScoreBeforeOpportunity - bestAlternative.ScoreBeforeOpportunity);
        return Math.Clamp(1m - (gap / ReplaceabilityWindow), 0m, 1m);
    }

    private static decimal CoverageLoss(decimal bestScore) =>
        Math.Clamp(Math.Max(6m, (bestScore - ViableScore) * 0.25m), 0m, MaxLossPerDefense);

    private static bool Overlaps(HashSet<string> consumedUnits, IEnumerable<string> otherUnits) =>
        otherUnits.Any(consumedUnits.Contains);

    private static string BuildSummary(
        decimal opportunityCost,
        int alternativesHere,
        int futureDefensesAtRisk)
    {
        if (opportunityCost <= 0m)
        {
            return futureDefensesAtRisk == 0
                ? "Sin coste de reserva relevante para las otras defensas visibles."
                : "Este núcleo también tiene valor futuro, pero aquí no existe una sustitución suficientemente cercana como para reservarlo.";
        }

        string defenseLabel = futureDefensesAtRisk == 1 ? "1 defensa futura" : $"{futureDefensesAtRisk} defensas futuras";
        string alternativeLabel = alternativesHere == 1 ? "1 alternativa viable" : $"{alternativesHere} alternativas viables";
        return $"Coste de oportunidad +{opportunityCost:0.#}: gastarlo aquí compromete {defenseLabel} y hay {alternativeLabel} que conservan sus unidades.";
    }

    private sealed record FutureLoss(Guid DefenseId, decimal Value, bool LosesCoverage);
}

internal readonly record struct GacStrategicCandidateKey(Guid DefenseId, Guid TeamPresetId);

internal sealed record GacStrategicCandidateSnapshot(
    Guid DefenseId,
    Guid TeamPresetId,
    decimal ScoreBeforeOpportunity,
    IReadOnlyCollection<string> UnitDefinitionIds)
{
    public GacStrategicCandidateKey Key => new(DefenseId, TeamPresetId);
}

internal sealed record GacOpportunityAssessment(
    decimal OpportunityCost,
    int AlternativesHere,
    int FutureDefensesAtRisk,
    string Summary)
{
    public static GacOpportunityAssessment None { get; } = new(
        0m,
        0,
        0,
        "Sin coste de reserva relevante para las otras defensas visibles.");
}
