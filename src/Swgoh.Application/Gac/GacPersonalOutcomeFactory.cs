using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal static class GacPersonalOutcomeFactory
{
    public static GacPersonalRoundOutcome FromState(GacPlannerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Dictionary<Guid, GacVisibleDefenseDetails> defenses = state.Plan.VisibleDefenses.ToDictionary(item => item.Id);
        GacPersonalAttackOutcome[] outcomes =
        [
            .. state.Plan.Attacks
                .Where(attack => attack.Status is GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed)
                .Where(attack => defenses.ContainsKey(attack.DefenseId))
                .OrderBy(attack => attack.DefenseId)
                .ThenBy(attack => attack.Attempt)
                .Select(attack => new GacPersonalAttackOutcome(
                    attack.Id,
                    attack.Attempt,
                    attack.Status,
                    ToDomain(defenses[attack.DefenseId].Squad, state.Plan.Format),
                    ToDomain(attack.Team.Squad, state.Plan.Format)))
        ];

        return new GacPersonalRoundOutcome(
            state.Plan.Id,
            state.Plan.PlayerAllyCode,
            state.Plan.OpponentAllyCode,
            state.Plan.EventInstanceId,
            state.Plan.RoundNumber,
            state.Plan.Format,
            outcomes,
            state.Plan.UpdatedAtUtc);
    }

    public static GacPersonalRoundOutcome FromPlan(
        GacRoundPlan plan,
        IReadOnlyDictionary<Guid, GacTeamPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(presets);

        Dictionary<Guid, GacVisibleDefense> defenses = plan.VisibleDefenses.ToDictionary(item => item.Id);
        GacPersonalAttackOutcome[] outcomes =
        [
            .. plan.Attacks
                .Where(attack => attack.Status is GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed)
                .Where(attack => defenses.ContainsKey(attack.DefenseId) && presets.ContainsKey(attack.TeamPresetId))
                .OrderBy(attack => attack.DefenseId)
                .ThenBy(attack => attack.Attempt)
                .Select(attack => new GacPersonalAttackOutcome(
                    attack.Id,
                    attack.Attempt,
                    attack.Status,
                    defenses[attack.DefenseId].Squad,
                    presets[attack.TeamPresetId].Squad))
        ];

        return new GacPersonalRoundOutcome(
            plan.Id,
            plan.PlayerAllyCode,
            plan.OpponentAllyCode,
            plan.EventInstanceId,
            plan.RoundNumber,
            plan.Format,
            outcomes,
            plan.UpdatedAtUtc);
    }

    private static GacPlannerSquad ToDomain(GacPlannerSquadDetails squad, GacFormat format) =>
        GacPlannerSquad.Create(
            format,
            squad.Leader.DefinitionId,
            squad.Members.Select(unit => unit.DefinitionId),
            squad.IsFleet);
}
