using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class GacAttackPlanner
{
    private string tacticalSort = "Recommended";
    private string tacticalFilter = "All";

    protected string TacticalSort => tacticalSort;

    protected string TacticalFilter => tacticalFilter;

    protected IReadOnlyCollection<VisibleDefenseDraft> TacticalVisibleDefenses
    {
        get
        {
            IEnumerable<VisibleDefenseDraft> filtered = visibleDefenses.Where(MatchesTacticalFilter);
            IOrderedEnumerable<VisibleDefenseDraft> ordered = tacticalSort switch
            {
                "Safest" => filtered
                    .OrderBy(IsDefeated)
                    .ThenBy(defense => TacticalAssessment(defense.Id).SortRank)
                    .ThenBy(defense => defense.Zone, StringComparer.OrdinalIgnoreCase),
                "Hardest" => filtered
                    .OrderBy(IsDefeated)
                    .ThenByDescending(defense => ActiveRiskRank(defense.Id))
                    .ThenBy(defense => defense.Zone, StringComparer.OrdinalIgnoreCase),
                "Zone" => filtered
                    .OrderBy(IsDefeated)
                    .ThenBy(defense => defense.Zone, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(defense => EnemyLeaderName(defense, FindVisibleDefense(defense.Id)), StringComparer.OrdinalIgnoreCase),
                "Unplanned" => filtered
                    .OrderBy(IsDefeated)
                    .ThenBy(defense => HasActivePlan(defense.Id))
                    .ThenBy(defense => TacticalAssessment(defense.Id).SortRank)
                    .ThenBy(defense => defense.Zone, StringComparer.OrdinalIgnoreCase),
                _ => filtered
                    .OrderBy(IsDefeated)
                    .ThenBy(defense => RecommendedPriority(defense.Id))
                    .ThenBy(defense => defense.Zone, StringComparer.OrdinalIgnoreCase)
            };

            return [.. ordered];
        }
    }

    protected void SetTacticalSort(string value)
    {
        tacticalSort = value is "Safest" or "Hardest" or "Zone" or "Unplanned"
            ? value
            : "Recommended";
    }

    protected void SetTacticalFilter(string value)
    {
        tacticalFilter = value is "Low" or "Medium" or "High" or "NoCounter" or "Unplanned"
            ? value
            : "All";
    }

    protected GacMatchupRiskAssessment TacticalAssessment(Guid defenseId)
    {
        GacPlannerApiClient.VisibleDefenseViewModel? details = FindVisibleDefense(defenseId);
        return GacMatchupRiskEvaluator.Evaluate(FindCounterHint(defenseId), details?.Defeated == true);
    }

    private bool MatchesTacticalFilter(VisibleDefenseDraft defense)
    {
        GacPlannerApiClient.CounterHintViewModel? hint = FindCounterHint(defense.Id);
        GacMatchupRiskAssessment assessment = TacticalAssessment(defense.Id);

        return tacticalFilter switch
        {
            "Low" => assessment.Tone == "low",
            "Medium" => assessment.Tone == "medium",
            "High" => assessment.Tone == "high",
            "NoCounter" => hint is null,
            "Unplanned" => !IsDefeated(defense) && !HasActivePlan(defense.Id),
            _ => true
        };
    }

    private int RecommendedPriority(Guid defenseId)
    {
        if (HasActivePlan(defenseId))
        {
            return TacticalAssessment(defenseId).SortRank;
        }

        GacMatchupRiskAssessment assessment = TacticalAssessment(defenseId);
        return assessment.Tone switch
        {
            "low" => 10,
            "medium" => 11,
            "high" => 12,
            _ => 13
        };
    }

    private int ActiveRiskRank(Guid defenseId)
    {
        GacMatchupRiskAssessment assessment = TacticalAssessment(defenseId);
        return assessment.Tone switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };
    }

    private bool IsDefeated(VisibleDefenseDraft defense) => FindVisibleDefense(defense.Id)?.Defeated == true;

    private bool HasActivePlan(Guid defenseId) => attacks.Any(attack =>
        attack.DefenseId == defenseId && attack.Status is "Planned" or "Won");

    private PlayerApiClient.RosterUnitViewModel? FindOwnRosterUnit(string definitionId) =>
        playerRoster.FirstOrDefault(unit => string.Equals(
            unit.DefinitionId,
            definitionId,
            StringComparison.OrdinalIgnoreCase));

    private PlayerApiClient.RosterUnitViewModel? FindOpponentRosterUnit(string definitionId) =>
        opponentRoster.FirstOrDefault(unit => string.Equals(
            unit.DefinitionId,
            definitionId,
            StringComparison.OrdinalIgnoreCase));

    private static bool DatacronNeedsReview(string status) =>
        !string.Equals(status, "NotRequired", StringComparison.OrdinalIgnoreCase);

    private static string FriendlyDatacronStatus(string status) => status switch
    {
        "Level9AvailableUnverified" => "nivel 9 disponible; verifica afinidad",
        "NoCandidate" => "no hay candidato claro",
        "NotRequired" => "no requerido",
        _ => "verifica aplicabilidad"
    };
}
