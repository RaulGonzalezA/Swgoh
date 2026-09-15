using Swgoh.Application.GameData;
using Swgoh.Domain.Conquest;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Conquest;

internal sealed partial class ConquestService
{
    private static CandidateUnit? ToCandidate(
        RosterUnit unit,
        GameDataCatalog catalog,
        IReadOnlyCollection<ConquestFeat> pending,
        ConquestPlan plan)
    {
        if (!catalog.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition) || definition.IsShip)
        {
            return null;
        }

        int currentStamina = plan.GetCurrentStamina(unit.DefinitionId);
        if (currentStamina == 0)
        {
            return null;
        }

        int expectedPostBattleStamina = Math.Max(0, currentStamina - plan.StaminaCostPerBattle);
        var view = new ConquestOptimizationUnit(
            unit.DefinitionId,
            definition.Name,
            definition.ThumbnailName,
            unit.RelicTier,
            unit.GalacticPower,
            unit.Stats?.Speed,
            definition.Factions,
            currentStamina,
            expectedPostBattleStamina,
            expectedPostBattleStamina < plan.ReserveFloorPercent);
        decimal baseWeight = pending
            .Where(feat => Matches(unit.DefinitionId, definition.Factions, feat.Rule))
            .Sum(feat => feat.Points / (decimal)Math.Max(1, feat.Remaining));
        decimal readinessWeight = 0.65m + (0.35m * currentStamina / 100m);
        return new CandidateUnit(unit, view, baseWeight * readinessWeight);
    }

    private static bool Matches(CandidateUnit candidate, ConquestFeatRule rule) =>
        Matches(candidate.Unit.DefinitionId, candidate.View.Factions, rule);

    private static bool Matches(
        string definitionId,
        IReadOnlyCollection<string> factions,
        ConquestFeatRule rule) => rule.Type switch
        {
            ConquestFeatRuleType.AnyCharacter => true,
            ConquestFeatRuleType.Faction => factions.Any(faction => string.Equals(
                faction,
                rule.Faction,
                StringComparison.OrdinalIgnoreCase)),
            ConquestFeatRuleType.SpecificUnits => rule.UnitDefinitionIds.Any(value => string.Equals(
                value,
                definitionId,
                StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

    private static decimal? AverageNullable(IEnumerable<decimal?> values)
    {
        decimal[] known = [.. values.Where(value => value is not null).Select(value => value!.Value)];
        return known.Length == 0 ? null : Math.Round(known.Average(), 1);
    }

    private static ConquestTeamRecommendation ToRecommendation(
        TeamCandidate team,
        int rank,
        int reserveFloorPercent)
    {
        string featNames = string.Join(", ", team.Contributions.Select(item => item.FeatName));
        string staminaNote = team.ReserveRiskUnits > 0
            ? $" Stamina media {team.AverageStamina:0}% → {team.PostBattleAverageStamina:0}%; {team.ReserveRiskUnits} unidad(es) quedarían por debajo de la reserva del {reserveFloorPercent}%."
            : $" Stamina media {team.AverageStamina:0}% → {team.PostBattleAverageStamina:0}%.";
        string diskNote = team.DiskLoadout is null
            ? string.Empty
            : $" Preset de discos recomendado: {team.DiskLoadout.LoadoutName} ({team.DiskLoadout.CapacityUsed}/{team.DiskLoadout.CapacityLimit}, bonus planificador {team.DiskLoadout.PlannerBonus:0.#}).";
        return new ConquestTeamRecommendation(
            rank,
            team.Score,
            team.FeatEfficiency,
            team.TeamGalacticPower,
            team.AverageSpeed,
            team.AverageStamina,
            team.PostBattleAverageStamina,
            team.StaminaOpportunityCost,
            team.ReserveRiskUnits,
            team.DiskLoadout,
            [.. team.Units.Select(candidate => candidate.View)],
            team.Contributions,
            $"Avanza {team.Contributions.Count} hazaña(s) en la misma batalla: {featNames}.{staminaNote}{diskNote}");
    }

    private static ConquestFeat ToDomain(SaveConquestFeat input) => ConquestFeat.Create(
        input.Id ?? Guid.NewGuid(),
        input.Name,
        input.Scope,
        input.Sector,
        input.Points,
        input.Target,
        input.Progress,
        input.ExpectedProgressPerBattle,
        ConquestFeatRule.Create(
            input.RuleType,
            input.Faction,
            input.UnitDefinitionIds,
            input.MinimumMatchingUnits));

    private static ConquestDataDisk ToDomain(SaveConquestDataDisk input) => ConquestDataDisk.Create(
        input.Id ?? Guid.NewGuid(),
        input.Name,
        input.CapacityCost,
        input.PlannerBonus,
        ConquestDataDiskTarget.Create(
            input.TargetType,
            input.Faction,
            input.UnitDefinitionIds,
            input.MinimumMatchingUnits),
        input.SupportedFeatIds,
        input.Notes);

    private static ConquestDiskLoadout ToDomain(SaveConquestDiskLoadout input) => ConquestDiskLoadout.Create(
        input.Id ?? Guid.NewGuid(),
        input.Name,
        input.DiskIds);

    private static ConquestPlanDetails ToDetails(ConquestPlan plan)
    {
        ConquestFeatDetails[] feats =
        [
            .. plan.Feats.Select(feat => new ConquestFeatDetails(
                feat.Id,
                feat.Name,
                feat.Scope,
                feat.Sector,
                feat.Points,
                feat.Target,
                feat.Progress,
                feat.Remaining,
                feat.ExpectedProgressPerBattle,
                feat.Rule,
                feat.IsComplete))
        ];
        return new ConquestPlanDetails(
            plan.Id,
            plan.AllyCode,
            plan.EventId,
            plan.Name,
            plan.Difficulty,
            feats,
            feats.Count(feat => feat.IsComplete),
            feats.Length,
            feats.Where(feat => feat.IsComplete).Sum(feat => feat.Points),
            feats.Where(feat => !feat.IsComplete).Sum(feat => feat.Points),
            plan.StaminaCostPerBattle,
            plan.ReserveFloorPercent,
            plan.Stamina,
            plan.DiskCapacityLimit,
            plan.DataDisks,
            plan.DiskLoadouts,
            plan.AvailableEnergy,
            plan.EnergyCostPerBattle,
            plan.CurrentRewardPoints,
            plan.TargetRewardPoints,
            plan.RewardTargetName,
            plan.UpdatedAtUtc);
    }

    private sealed record CandidateUnit(
        RosterUnit Unit,
        ConquestOptimizationUnit View,
        decimal FeatWeight);

    private sealed record TeamCandidate(
        IReadOnlyCollection<CandidateUnit> Units,
        IReadOnlyCollection<ConquestFeatContribution> Contributions,
        decimal Score,
        decimal FeatEfficiency,
        long TeamGalacticPower,
        decimal? AverageSpeed,
        decimal AverageStamina,
        decimal PostBattleAverageStamina,
        decimal StaminaOpportunityCost,
        int ReserveRiskUnits,
        ConquestDiskRecommendation? DiskLoadout);
}
