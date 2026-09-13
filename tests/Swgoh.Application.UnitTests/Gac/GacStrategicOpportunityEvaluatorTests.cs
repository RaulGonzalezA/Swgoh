using Swgoh.Application.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacStrategicOpportunityEvaluatorTests
{
    [Fact]
    public void Evaluate_WhenCandidateIsReplaceableHereButCriticalLater_AddsOpportunityCost()
    {
        Guid easyDefense = Guid.NewGuid();
        Guid hardDefense = Guid.NewGuid();
        Guid premiumTeam = Guid.NewGuid();
        Guid easyAlternative = Guid.NewGuid();
        Guid hardAlternative = Guid.NewGuid();
        GacStrategicCandidateSnapshot[] candidates =
        [
            Candidate(easyDefense, premiumTeam, 80m, "A", "B", "C"),
            Candidate(easyDefense, easyAlternative, 76m, "D", "E", "F"),
            Candidate(hardDefense, premiumTeam, 90m, "A", "B", "C"),
            Candidate(hardDefense, hardAlternative, 45m, "G", "H", "I")
        ];

        IReadOnlyDictionary<GacStrategicCandidateKey, GacOpportunityAssessment> result =
            GacStrategicOpportunityEvaluator.Evaluate(candidates);

        GacOpportunityAssessment assessment = result[new GacStrategicCandidateKey(easyDefense, premiumTeam)];
        Assert.True(assessment.OpportunityCost > 0m);
        Assert.Equal(1, assessment.AlternativesHere);
        Assert.Equal(1, assessment.FutureDefensesAtRisk);
        Assert.Contains("Coste de oportunidad", assessment.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WhenFutureAlternativeSharesAConsumedUnit_TreatsItAsUnavailable()
    {
        Guid firstDefense = Guid.NewGuid();
        Guid secondDefense = Guid.NewGuid();
        Guid premiumTeam = Guid.NewGuid();
        GacStrategicCandidateSnapshot[] candidates =
        [
            Candidate(firstDefense, premiumTeam, 72m, "A", "B", "C"),
            Candidate(firstDefense, Guid.NewGuid(), 70m, "D", "E", "F"),
            Candidate(secondDefense, Guid.NewGuid(), 82m, "A", "G", "H"),
            Candidate(secondDefense, Guid.NewGuid(), 50m, "I", "J", "K")
        ];

        IReadOnlyDictionary<GacStrategicCandidateKey, GacOpportunityAssessment> result =
            GacStrategicOpportunityEvaluator.Evaluate(candidates);

        GacOpportunityAssessment assessment = result[new GacStrategicCandidateKey(firstDefense, premiumTeam)];
        Assert.True(assessment.OpportunityCost > 0m);
        Assert.Equal(1, assessment.FutureDefensesAtRisk);
    }

    [Fact]
    public void Evaluate_WhenCandidateCannotBeReplacedOnCurrentDefense_DoesNotPenalizeUsingItNow()
    {
        Guid currentDefense = Guid.NewGuid();
        Guid futureDefense = Guid.NewGuid();
        Guid premiumTeam = Guid.NewGuid();
        GacStrategicCandidateSnapshot[] candidates =
        [
            Candidate(currentDefense, premiumTeam, 88m, "A", "B", "C"),
            Candidate(currentDefense, Guid.NewGuid(), 40m, "D", "E", "F"),
            Candidate(futureDefense, premiumTeam, 90m, "A", "B", "C"),
            Candidate(futureDefense, Guid.NewGuid(), 45m, "G", "H", "I")
        ];

        IReadOnlyDictionary<GacStrategicCandidateKey, GacOpportunityAssessment> result =
            GacStrategicOpportunityEvaluator.Evaluate(candidates);

        GacOpportunityAssessment assessment = result[new GacStrategicCandidateKey(currentDefense, premiumTeam)];
        Assert.Equal(0m, assessment.OpportunityCost);
        Assert.Equal(1, assessment.FutureDefensesAtRisk);
        Assert.Contains("no existe una sustitución suficientemente cercana", assessment.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WhenNoOtherDefenseNeedsTheUnits_ReturnsNoOpportunityCost()
    {
        Guid currentDefense = Guid.NewGuid();
        Guid otherDefense = Guid.NewGuid();
        Guid premiumTeam = Guid.NewGuid();
        GacStrategicCandidateSnapshot[] candidates =
        [
            Candidate(currentDefense, premiumTeam, 70m, "A", "B", "C"),
            Candidate(currentDefense, Guid.NewGuid(), 68m, "D", "E", "F"),
            Candidate(otherDefense, Guid.NewGuid(), 80m, "G", "H", "I")
        ];

        IReadOnlyDictionary<GacStrategicCandidateKey, GacOpportunityAssessment> result =
            GacStrategicOpportunityEvaluator.Evaluate(candidates);

        GacOpportunityAssessment assessment = result[new GacStrategicCandidateKey(currentDefense, premiumTeam)];
        Assert.Equal(0m, assessment.OpportunityCost);
        Assert.Equal(0, assessment.FutureDefensesAtRisk);
    }

    private static GacStrategicCandidateSnapshot Candidate(
        Guid defenseId,
        Guid teamId,
        decimal score,
        params string[] units) =>
        new(defenseId, teamId, score, units);
}
