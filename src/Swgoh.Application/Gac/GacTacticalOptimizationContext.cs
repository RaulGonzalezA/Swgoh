using Swgoh.Domain.Players;

namespace Swgoh.Application.Gac;

internal sealed class GacTacticalOptimizationContext
{
    private readonly IReadOnlyDictionary<string, RosterUnit> playerUnits;
    private readonly IReadOnlyDictionary<string, RosterUnit> opponentUnits;
    private readonly IReadOnlyCollection<PlayerDatacron> playerDatacrons;

    private GacTacticalOptimizationContext(
        IReadOnlyDictionary<string, RosterUnit> playerUnits,
        IReadOnlyDictionary<string, RosterUnit> opponentUnits,
        IReadOnlyCollection<PlayerDatacron> playerDatacrons)
    {
        this.playerUnits = playerUnits;
        this.opponentUnits = opponentUnits;
        this.playerDatacrons = playerDatacrons;
    }

    public static GacTacticalOptimizationContext Empty { get; } = new(
        new Dictionary<string, RosterUnit>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, RosterUnit>(StringComparer.OrdinalIgnoreCase),
        []);

    public static GacTacticalOptimizationContext From(PlayerProfile? player, PlayerProfile? opponent) => new(
        ToRosterLookup(player),
        ToRosterLookup(opponent),
        player?.Datacrons ?? []);

    public GacTacticalEvaluation Evaluate(
        GacVisibleDefenseDetails defense,
        GacTeamPresetDetails preset,
        bool requiresDatacronVerification)
    {
        RosterUnit[] attackers = ResolveUnits(preset.Squad, playerUnits);
        RosterUnit[] defenders = ResolveUnits(defense.Squad, opponentUnits);
        decimal? teamAverageSpeed = Average(attackers, unit => unit.Stats?.Speed);
        decimal? defenseAverageSpeed = Average(defenders, unit => unit.Stats?.Speed);
        decimal? teamModSpeed = Average(attackers, unit => unit.Mods?.SpeedBonus);
        decimal? defenseModSpeed = Average(defenders, unit => unit.Mods?.SpeedBonus);

        decimal adjustment = 0m;
        var reasons = new List<string>();

        if (teamAverageSpeed is decimal attackerSpeed && defenseAverageSpeed is decimal defenderSpeed)
        {
            decimal speedAdjustment = Math.Clamp((attackerSpeed - defenderSpeed) / 25m, -1m, 1m) * 3m;
            adjustment += speedAdjustment;
            if (Math.Abs(speedAdjustment) >= 0.5m)
            {
                reasons.Add($"velocidad {attackerSpeed:0.#} vs {defenderSpeed:0.#}");
            }
        }

        decimal? attackerEffectiveHealth = Sum(attackers, EffectiveHealth);
        decimal? defenderEffectiveHealth = Sum(defenders, EffectiveHealth);
        if (attackerEffectiveHealth is > 0m && defenderEffectiveHealth is > 0m)
        {
            decimal ratio = attackerEffectiveHealth.Value / defenderEffectiveHealth.Value;
            decimal healthAdjustment = Math.Clamp((ratio - 1m) * 2m, -2m, 2m);
            adjustment += healthAdjustment;
            if (Math.Abs(healthAdjustment) >= 0.75m)
            {
                reasons.Add(ratio >= 1m ? "más aguante efectivo" : "menos aguante efectivo");
            }
        }

        decimal? attackerDamage = Sum(attackers, BestDamage);
        decimal? defenderDamage = Sum(defenders, BestDamage);
        if (attackerDamage is > 0m && defenderDamage is > 0m)
        {
            decimal ratio = attackerDamage.Value / defenderDamage.Value;
            decimal damageAdjustment = Math.Clamp((ratio - 1m) * 2m, -2m, 2m);
            adjustment += damageAdjustment;
            if (Math.Abs(damageAdjustment) >= 0.75m)
            {
                reasons.Add(ratio >= 1m ? "más daño bruto" : "menos daño bruto");
            }
        }

        decimal? attackerModCompleteness = Average(attackers, unit =>
            unit.Mods is null ? null : Math.Clamp(unit.Mods.EquippedCount / 6m, 0m, 1m));
        decimal? defenderModCompleteness = Average(defenders, unit =>
            unit.Mods is null ? null : Math.Clamp(unit.Mods.EquippedCount / 6m, 0m, 1m));
        if (attackerModCompleteness is decimal ownMods && defenderModCompleteness is decimal rivalMods)
        {
            adjustment += Math.Clamp((ownMods - rivalMods) * 3m, -1.5m, 1.5m);
        }

        if (teamModSpeed is decimal ownModSpeed && defenseModSpeed is decimal rivalModSpeed)
        {
            decimal modSpeedAdjustment = Math.Clamp((ownModSpeed - rivalModSpeed) / 50m, -1m, 1m) * 2m;
            adjustment += modSpeedAdjustment;
            if (Math.Abs(modSpeedAdjustment) >= 0.75m)
            {
                reasons.Add($"mods de velocidad {ownModSpeed:+0.#;-0.#;0} vs {rivalModSpeed:+0.#;-0.#;0}");
            }
        }

        (decimal datacronAdjustment, string datacronStatus, string? datacronReason) = EvaluateDatacron(
            preset,
            attackers,
            requiresDatacronVerification);
        adjustment += datacronAdjustment;
        if (datacronReason is not null)
        {
            reasons.Add(datacronReason);
        }

        decimal roundedAdjustment = Math.Round(Math.Clamp(adjustment, -8m, 8m), 1);
        string summary = reasons.Count == 0
            ? "Sin datos tácticos suficientes para ajustar el counter."
            : $"Ajuste táctico {roundedAdjustment:+0.#;-0.#;0}: {string.Join(", ", reasons)}.";

        return new GacTacticalEvaluation(
            roundedAdjustment,
            teamAverageSpeed,
            defenseAverageSpeed,
            teamModSpeed,
            defenseModSpeed,
            datacronStatus,
            summary);
    }

    private (decimal Adjustment, string Status, string? Reason) EvaluateDatacron(
        GacTeamPresetDetails preset,
        IReadOnlyCollection<RosterUnit> attackers,
        bool required)
    {
        if (preset.Squad.IsFleet || !required)
        {
            return (0m, "NotRequired", null);
        }

        int minimumRelic = attackers
            .Where(unit => !unit.IsShip)
            .Select(unit => unit.RelicTier)
            .DefaultIfEmpty(0)
            .Min();
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        PlayerDatacron[] candidates =
        [
            .. playerDatacrons
                .Where(datacron =>
                    !datacron.IsExpired(nowUtc) &&
                    datacron.Tier >= 3 &&
                    (datacron.HighestRequiredRelicTier == 0 || datacron.HighestRequiredRelicTier <= minimumRelic))
                .OrderByDescending(datacron => datacron.Tier)
        ];
        if (candidates.Length == 0)
        {
            return (-5m, "NoCandidate", "no hay datacron elegible detectado para un counter que requiere verificación");
        }

        int bestTier = candidates[0].Tier;
        decimal adjustment = bestTier >= 9 ? 1.5m : bestTier >= 6 ? 1m : 0.5m;
        string status = bestTier >= 9
            ? "Level9AvailableUnverified"
            : bestTier >= 6 ? "Level6AvailableUnverified" : "AvailableUnverified";
        return (
            adjustment,
            status,
            $"hay datacron nivel {bestTier} potencialmente utilizable; afinidad exacta pendiente de verificar");
    }

    private static IReadOnlyDictionary<string, RosterUnit> ToRosterLookup(PlayerProfile? player) =>
        player?.Roster
            .GroupBy(unit => unit.DefinitionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
        ?? new Dictionary<string, RosterUnit>(StringComparer.OrdinalIgnoreCase);

    private static RosterUnit[] ResolveUnits(
        GacPlannerSquadDetails squad,
        IReadOnlyDictionary<string, RosterUnit> roster) =>
        [
            .. squad.AllUnits
                .Select(unit => roster.GetValueOrDefault(unit.DefinitionId))
                .Where(unit => unit is not null)
                .Select(unit => unit!)
        ];

    private static decimal? Average(
        IEnumerable<RosterUnit> units,
        Func<RosterUnit, decimal?> selector)
    {
        decimal[] values = [.. units.Select(selector).Where(value => value is not null).Select(value => value!.Value)];
        return values.Length == 0 ? null : Math.Round(values.Average(), 1);
    }

    private static decimal? Sum(
        IEnumerable<RosterUnit> units,
        Func<RosterUnit, decimal?> selector)
    {
        decimal[] values = [.. units.Select(selector).Where(value => value is not null).Select(value => value!.Value)];
        return values.Length == 0 ? null : values.Sum();
    }

    private static decimal? EffectiveHealth(RosterUnit unit)
    {
        if (unit.Stats is null || (unit.Stats.Health is null && unit.Stats.Protection is null))
        {
            return null;
        }

        return (unit.Stats.Health ?? 0m) + (unit.Stats.Protection ?? 0m);
    }

    private static decimal? BestDamage(RosterUnit unit)
    {
        if (unit.Stats is null)
        {
            return null;
        }

        decimal physical = unit.Stats.PhysicalDamage ?? 0m;
        decimal special = unit.Stats.SpecialDamage ?? 0m;
        decimal best = Math.Max(physical, special);
        return best <= 0m ? null : best;
    }
}

internal sealed record GacTacticalEvaluation(
    decimal Adjustment,
    decimal? TeamAverageSpeed,
    decimal? DefenseAverageSpeed,
    decimal? TeamModSpeedBonus,
    decimal? DefenseModSpeedBonus,
    string DatacronStatus,
    string Summary);