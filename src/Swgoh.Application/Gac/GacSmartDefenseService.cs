using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacSmartDefenseService
{
    Task<GacSmartDefenseGenerationResult> GenerateCurrentAsync(
        long allyCode,
        bool apply,
        CancellationToken cancellationToken = default);
}

public sealed record GacSmartDefenseAssignment(
    int Position,
    string Zone,
    Guid TeamPresetId,
    string TeamName,
    bool Pinned,
    bool IsFleet,
    long GalacticPower,
    decimal Score,
    decimal DefensiveValue,
    decimal OffensiveOpportunityCost,
    string Confidence,
    bool ContainsGalacticLegend,
    int OmicronCount,
    int EligibleDatacronTier,
    int OpponentSamples,
    int PersonalSamples,
    IReadOnlyCollection<string> Reasons);

public sealed record GacSmartDefenseGenerationResult(
    GacFormat Format,
    bool Applied,
    IReadOnlyCollection<GacSmartDefenseAssignment> Assignments,
    IReadOnlyCollection<string> Warnings,
    DateTimeOffset? PlanUpdatedAtUtc,
    int OpponentRoundsAnalyzed,
    decimal? OpponentFullClearRate,
    string IntelligenceMode);

internal sealed class GacSmartDefenseService(
    GacDefenseStrategyService strategyService,
    IGacPlannerService plannerService,
    ICurrentGacScoutingService scoutingService,
    IGacPersonalLearningService personalLearningService) : IGacSmartDefenseService
{
    private const int HistoryRoundLimit = 30;

    public async Task<GacSmartDefenseGenerationResult> GenerateCurrentAsync(
        long allyCode,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        GacPlannerLookup lookup = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (!lookup.IsAvailable || lookup.State is null)
        {
            throw new InvalidOperationException(lookup.Message ?? "No active GAC round is available.");
        }

        GacPlannerState state = lookup.State;
        Task<GacDefenseStrategySnapshot> strategyTask = strategyService.GetAsync(
            allyCode,
            state.Plan.Format,
            cancellationToken);
        Task<CurrentGacScoutingResult> scoutingTask = scoutingService.GetAsync(
            allyCode,
            state.Plan.Format,
            HistoryRoundLimit,
            cancellationToken);
        Task<IReadOnlyCollection<GacPersonalMatchupStatistics>> personalTask = personalLearningService
            .GetStatisticsAsync(allyCode, state.Plan.Format, cancellationToken);

        await Task.WhenAll(strategyTask, scoutingTask, personalTask).ConfigureAwait(false);
        GacDefenseStrategySnapshot strategy = await strategyTask.ConfigureAwait(false);
        CurrentGacScoutingResult scouting = await scoutingTask.ConfigureAwait(false);
        IReadOnlyCollection<GacPersonalMatchupStatistics> personal = await personalTask.ConfigureAwait(false);

        SmartGeneration generation = Generate(
            strategy.Profile,
            state.Presets,
            scouting,
            personal,
            state.PlayerDatacrons);

        if (!apply)
        {
            return ToResult(state.Plan.Format, false, generation, state.Plan.UpdatedAtUtc, scouting);
        }

        IReadOnlyCollection<SaveGacOwnDefenseAssignment> ownDefenses =
        [
            .. generation.Assignments.Select(item =>
            {
                GacOwnDefenseAssignmentDetails? existing = state.Plan.OwnDefenses.FirstOrDefault(defense =>
                    defense.Team.Id == item.TeamPresetId &&
                    string.Equals(defense.Zone, item.Zone, StringComparison.OrdinalIgnoreCase));
                return new SaveGacOwnDefenseAssignment(existing?.Id, item.Zone, item.TeamPresetId);
            })
        ];
        IReadOnlyCollection<SaveGacVisibleDefense> visibleDefenses =
        [
            .. state.Plan.VisibleDefenses.Select(item => new SaveGacVisibleDefense(
                item.Id,
                item.Zone,
                item.Label,
                item.Squad.Leader.DefinitionId,
                [.. item.Squad.Members.Select(unit => unit.DefinitionId)],
                item.Squad.IsFleet))
        ];
        IReadOnlyCollection<SaveGacAttackAssignment> attacks =
        [
            .. state.Plan.Attacks.Select(item => new SaveGacAttackAssignment(
                item.Id,
                item.DefenseId,
                item.Team.Id,
                item.Attempt,
                item.Status,
                item.Notes))
        ];

        GacPlannerLookup saved = await plannerService
            .SaveCurrentAsync(
                allyCode,
                new SaveCurrentGacRoundPlan(ownDefenses, visibleDefenses, attacks, state.Plan.Version),
                cancellationToken)
            .ConfigureAwait(false);
        if (!saved.IsAvailable || saved.State is null)
        {
            throw new InvalidOperationException(saved.Message ?? "The smart defense could not be applied.");
        }

        return ToResult(state.Plan.Format, true, generation, saved.State.Plan.UpdatedAtUtc, scouting);
    }

    internal static SmartGeneration Generate(
        GacDefenseStrategyProfile profile,
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        CurrentGacScoutingResult? scouting,
        IReadOnlyCollection<GacPersonalMatchupStatistics>? personalStatistics,
        IReadOnlyCollection<GacPlannerDatacronDetails>? datacrons)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(presets);

        IReadOnlyCollection<GacPersonalMatchupStatistics> personal = personalStatistics ?? [];
        IReadOnlyCollection<GacPlannerDatacronDetails> playerDatacrons = datacrons ?? [];
        CurrentGacBattlePlan? battlePlan = scouting?.BattlePlan;
        OpponentScoutingReport? opponentHistory = scouting?.Scouting;
        Dictionary<Guid, GacTeamPresetDetails> byId = presets.ToDictionary(preset => preset.Id);
        HashSet<Guid> reserved = profile.ReservedAttackPresetIds.ToHashSet();
        var usedPresets = new HashSet<Guid>();
        var usedUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignments = new List<GacSmartDefenseAssignment>();
        var warnings = new List<string>();

        if (opponentHistory is null || opponentHistory.RoundsAnalyzed == 0)
        {
            warnings.Add("No hay histórico suficiente del rival; el motor ponderará más tu roster y tus reservas ofensivas.");
        }
        if (personal.Count == 0)
        {
            warnings.Add("Aún no hay aprendizaje personal suficiente; el coste ofensivo se apoya en reservas y counters del War Room.");
        }
        if (playerDatacrons.Count == 0)
        {
            warnings.Add("No se han detectado datacrons del jugador para ajustar la defensa.");
        }

        decimal maxCharacterPower = presets
            .Where(preset => !preset.Squad.IsFleet)
            .Select(TeamPower)
            .DefaultIfEmpty(1)
            .Max();
        decimal maxFleetPower = presets
            .Where(preset => preset.Squad.IsFleet)
            .Select(TeamPower)
            .DefaultIfEmpty(1)
            .Max();
        SmartSignals signals = BuildSignals(battlePlan, opponentHistory, personal, playerDatacrons);

        foreach (GacDefenseTemplateSlot slot in profile.Slots.OrderBy(slot => slot.Position))
        {
            GacTeamPresetDetails? selected = null;
            CandidateScore? selectedScore = null;
            bool pinned = false;

            if (slot.PinnedTeamPresetId is Guid pinnedId)
            {
                pinned = true;
                if (reserved.Contains(pinnedId))
                {
                    warnings.Add($"Slot {slot.Position}: el equipo fijado está reservado para ataque.");
                    continue;
                }

                byId.TryGetValue(pinnedId, out selected);
                if (selected is null)
                {
                    warnings.Add($"Slot {slot.Position}: el equipo fijado ya no existe.");
                    continue;
                }

                if (!MatchesZone(selected, slot.Zone))
                {
                    warnings.Add($"Slot {slot.Position}: {selected.Name} no coincide con el tipo de zona {slot.Zone}.");
                    continue;
                }

                if (Conflicts(selected, usedPresets, usedUnits))
                {
                    warnings.Add($"Slot {slot.Position}: {selected.Name} comparte unidades con otra defensa ya elegida.");
                    continue;
                }

                selectedScore = ScoreCandidate(
                    selected,
                    selected.Squad.IsFleet ? maxFleetPower : maxCharacterPower,
                    signals);
            }
            else
            {
                (GacTeamPresetDetails Preset, CandidateScore Score)? best = presets
                    .Where(preset => !reserved.Contains(preset.Id))
                    .Where(preset => MatchesZone(preset, slot.Zone))
                    .Where(preset => !Conflicts(preset, usedPresets, usedUnits))
                    .Select(preset => (
                        Preset: preset,
                        Score: ScoreCandidate(
                            preset,
                            preset.Squad.IsFleet ? maxFleetPower : maxCharacterPower,
                            signals)))
                    .OrderByDescending(candidate => candidate.Score.FinalScore)
                    .ThenByDescending(candidate => candidate.Score.DefensiveValue)
                    .ThenByDescending(candidate => TeamPower(candidate.Preset))
                    .ThenBy(candidate => candidate.Preset.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(candidate => ((GacTeamPresetDetails Preset, CandidateScore Score)?)candidate)
                    .FirstOrDefault();
                if (best is not null)
                {
                    selected = best.Value.Preset;
                    selectedScore = best.Value.Score;
                }
            }

            if (selected is null || selectedScore is null)
            {
                warnings.Add($"Slot {slot.Position}: no hay un equipo compatible disponible para {slot.Zone}.");
                continue;
            }

            usedPresets.Add(selected.Id);
            foreach (GacPlannerUnitDetails unit in selected.Squad.AllUnits)
            {
                usedUnits.Add(unit.DefinitionId);
            }

            assignments.Add(new GacSmartDefenseAssignment(
                slot.Position,
                slot.Zone,
                selected.Id,
                selected.Name,
                pinned,
                selected.Squad.IsFleet,
                TeamPower(selected),
                selectedScore.FinalScore,
                selectedScore.DefensiveValue,
                selectedScore.OffensiveOpportunityCost,
                selectedScore.Confidence,
                selectedScore.ContainsGalacticLegend,
                selectedScore.OmicronCount,
                selectedScore.EligibleDatacronTier,
                selectedScore.OpponentSamples,
                selectedScore.PersonalSamples,
                selectedScore.Reasons));
        }

        return new SmartGeneration(assignments, warnings);
    }

    private static CandidateScore ScoreCandidate(
        GacTeamPresetDetails preset,
        decimal maxComparablePower,
        SmartSignals signals)
    {
        decimal power = TeamPower(preset);
        decimal powerRatio = maxComparablePower <= 0m ? 0m : Math.Clamp(power / maxComparablePower, 0m, 1m);
        int omicrons = preset.Squad.AllUnits.Sum(unit => unit.OmicronCount ?? 0);
        HashSet<string> unitIds = preset.Squad.AllUnits
            .Select(unit => unit.DefinitionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool hasGl = unitIds.Any(signals.GalacticLegendUnitIds.Contains);
        int datacronTier = BestEligibleDatacronTier(preset, signals.Datacrons);

        decimal intent = preset.Use switch
        {
            GacPlannerTeamUse.Defense => 20m,
            GacPlannerTeamUse.Flexible => 8m,
            _ => -6m
        };
        decimal powerValue = Math.Round(30m * powerRatio, 1);
        decimal omicronValue = preset.Use == GacPlannerTeamUse.Offense
            ? Math.Min(5m, omicrons * 2m)
            : Math.Min(12m, omicrons * 3m);
        decimal glValue = hasGl ? 12m : 0m;
        decimal datacronValue = Math.Min(8m, datacronTier * 0.9m);

        GacCounterPatternDetails[] opponentPatterns =
        [
            .. signals.OpponentCounterPatterns.Where(pattern =>
                pattern.IsFleet == preset.Squad.IsFleet &&
                pattern.DefenderLeader.DefinitionId.Equals(
                    preset.Squad.Leader.DefinitionId,
                    StringComparison.OrdinalIgnoreCase))
        ];
        int opponentSamples = opponentPatterns.Sum(pattern => pattern.Uses);
        decimal opponentAdjustment = 0m;
        if (opponentSamples > 0)
        {
            decimal weightedWinRate = opponentPatterns.Sum(pattern => pattern.WinRate * pattern.Uses) / opponentSamples;
            opponentAdjustment = Math.Clamp((50m - weightedWinRate) / 4m, -12m, 12m);
        }

        decimal pressureValue = 0m;
        if (signals.OpponentFullClearRate is decimal fullClear)
        {
            decimal normalized = fullClear > 1m ? fullClear / 100m : fullClear;
            pressureValue = Math.Clamp((normalized - 0.5m) * 10m * powerRatio, -3m, 5m);
        }

        decimal reserveCost = 0m;
        foreach (GacBattleAttackReserve reserve in signals.AttackReserves.Where(reserve => unitIds.Contains(reserve.Unit.DefinitionId)))
        {
            reserveCost += reserve.Role switch
            {
                "GlAnswer" => 12m,
                "GacSpecialist" => 8m,
                "FleetAnchor" => 7m,
                _ => 5m
            };
        }
        reserveCost = Math.Min(24m, reserveCost);

        decimal counterCost = Math.Min(
            16m,
            unitIds.Sum(id => signals.CounterPreservationCost.GetValueOrDefault(id)));

        GacPersonalMatchupStatistics? personal = signals.PersonalStatistics
            .Where(stat => stat.IsFleet == preset.Squad.IsFleet)
            .Where(stat => SetEquals(stat.AttackerDefinitionIds, unitIds))
            .OrderByDescending(stat => stat.Uses)
            .FirstOrDefault();
        int personalSamples = personal?.Uses ?? 0;
        decimal personalCost = 0m;
        if (personal is not null && personal.Uses > 0)
        {
            decimal confidenceWeight = Math.Min(1m, personal.Uses / 5m);
            personalCost = Math.Round(personal.WinRate * 10m * confidenceWeight, 1);
            if (personal.OneShotRate is decimal oneShotRate)
            {
                personalCost += Math.Round(oneShotRate * 4m * confidenceWeight, 1);
            }
        }

        decimal useCost = preset.Use switch
        {
            GacPlannerTeamUse.Offense => 8m,
            GacPlannerTeamUse.Flexible => 3m,
            _ => 0m
        };
        decimal offensiveCost = Math.Round(useCost + reserveCost + counterCost + personalCost, 1);
        decimal defensiveValue = Math.Round(
            intent + powerValue + omicronValue + glValue + datacronValue + opponentAdjustment + pressureValue,
            1);
        decimal finalScore = Math.Round(Math.Clamp(20m + defensiveValue - offensiveCost, 0m, 100m), 1);

        var reasons = new List<string>
        {
            $"Potencia relativa {powerRatio:P0}: +{powerValue:0.#}.",
            $"Perfil {UseLabel(preset.Use)}: {intent:+0.#;-0.#;0}."
        };
        if (omicrons > 0)
        {
            reasons.Add($"{omicrons} omicron(s): +{omicronValue:0.#} valor defensivo.");
        }
        if (hasGl)
        {
            reasons.Add("Incluye Leyenda Galáctica: +12 valor defensivo, compensado si está reservada como respuesta ofensiva.");
        }
        if (datacronTier > 0)
        {
            reasons.Add($"Datacron elegible hasta nivel {datacronTier}: +{datacronValue:0.#}.");
        }
        if (opponentSamples > 0)
        {
            reasons.Add($"Histórico del rival contra este líder: {opponentSamples} muestra(s), ajuste {opponentAdjustment:+0.#;-0.#;0}.");
        }
        if (reserveCost + counterCost > 0m)
        {
            reasons.Add($"Coste por reservar respuestas/counters: -{reserveCost + counterCost:0.#}.");
        }
        if (personalSamples > 0)
        {
            reasons.Add($"Tu rendimiento ofensivo con este equipo ({personalSamples} uso(s)) añade -{personalCost:0.#} de coste de oportunidad.");
        }

        string confidence = Confidence(signals.OpponentRoundsAnalyzed, opponentSamples, personalSamples);
        return new CandidateScore(
            finalScore,
            defensiveValue,
            offensiveCost,
            confidence,
            hasGl,
            omicrons,
            datacronTier,
            opponentSamples,
            personalSamples,
            reasons);
    }

    private static SmartSignals BuildSignals(
        CurrentGacBattlePlan? battlePlan,
        OpponentScoutingReport? opponentHistory,
        IReadOnlyCollection<GacPersonalMatchupStatistics> personal,
        IReadOnlyCollection<GacPlannerDatacronDetails> datacrons)
    {
        IReadOnlyCollection<GacBattleAttackReserve> reserves = battlePlan?.AttackReserves ?? [];
        var glIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GacBattleAttackReserve reserve in reserves.Where(item => item.Unit.IsGalacticLegend))
        {
            glIds.Add(reserve.Unit.DefinitionId);
        }

        var counterCosts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (GacBattleCounterSuggestion suggestion in battlePlan?.CounterSuggestions ?? [])
        {
            decimal weight = suggestion.Confidence switch
            {
                "High" => 7m,
                "Medium" => 5m,
                _ => 3m
            };
            if (suggestion.Source == "HistoricalCounterPattern")
            {
                weight += 2m;
            }

            IEnumerable<GacBattleUnit> units = suggestion.RecommendedTeam is { Count: > 0 }
                ? suggestion.RecommendedTeam
                : suggestion.CandidateAnchors;
            foreach (GacBattleUnit unit in units)
            {
                if (unit.IsGalacticLegend)
                {
                    glIds.Add(unit.DefinitionId);
                }
                counterCosts[unit.DefinitionId] = Math.Max(counterCosts.GetValueOrDefault(unit.DefinitionId), weight);
            }
        }

        return new SmartSignals(
            reserves,
            counterCosts,
            glIds,
            opponentHistory?.CounterPatterns ?? [],
            opponentHistory?.RoundsAnalyzed ?? 0,
            opponentHistory?.FullClearRate,
            personal,
            datacrons);
    }

    private static int BestEligibleDatacronTier(
        GacTeamPresetDetails preset,
        IReadOnlyCollection<GacPlannerDatacronDetails> datacrons)
    {
        if (preset.Squad.IsFleet || datacrons.Count == 0)
        {
            return 0;
        }

        int minimumRelic = preset.Squad.AllUnits
            .Select(unit => unit.RelicTier ?? 0)
            .DefaultIfEmpty(0)
            .Min();
        return datacrons
            .Where(datacron => !datacron.Locked && datacron.HighestRequiredRelicTier <= minimumRelic)
            .Select(datacron => datacron.Tier)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static GacSmartDefenseGenerationResult ToResult(
        GacFormat format,
        bool applied,
        SmartGeneration generation,
        DateTimeOffset? updatedAtUtc,
        CurrentGacScoutingResult scouting) => new(
        format,
        applied,
        generation.Assignments,
        generation.Warnings,
        updatedAtUtc,
        scouting.Scouting?.RoundsAnalyzed ?? 0,
        scouting.Scouting?.FullClearRate,
        "SmartBalanced");

    private static bool SetEquals(IReadOnlyCollection<string> source, HashSet<string> target) =>
        source.Count == target.Count && source.All(target.Contains);

    private static bool MatchesZone(GacTeamPresetDetails preset, string zone) =>
        preset.Squad.IsFleet == IsFleetZone(zone);

    private static bool IsFleetZone(string zone) =>
        zone.Contains("flota", StringComparison.OrdinalIgnoreCase) ||
        zone.Contains("fleet", StringComparison.OrdinalIgnoreCase);

    private static bool Conflicts(
        GacTeamPresetDetails preset,
        HashSet<Guid> usedPresets,
        HashSet<string> usedUnits) =>
        usedPresets.Contains(preset.Id) ||
        preset.Squad.AllUnits.Any(unit => usedUnits.Contains(unit.DefinitionId));

    private static long TeamPower(GacTeamPresetDetails preset) =>
        preset.Squad.AllUnits.Sum(unit => unit.GalacticPower ?? 0L);

    private static string Confidence(int rounds, int opponentSamples, int personalSamples) =>
        rounds >= 8 && (opponentSamples >= 3 || personalSamples >= 3)
            ? "High"
            : rounds > 0 || opponentSamples > 0 || personalSamples > 0
                ? "Medium"
                : "Low";

    private static string UseLabel(GacPlannerTeamUse use) => use switch
    {
        GacPlannerTeamUse.Defense => "Defensa",
        GacPlannerTeamUse.Flexible => "Flexible",
        _ => "Ataque"
    };

    internal sealed record SmartGeneration(
        IReadOnlyCollection<GacSmartDefenseAssignment> Assignments,
        IReadOnlyCollection<string> Warnings);

    private sealed record CandidateScore(
        decimal FinalScore,
        decimal DefensiveValue,
        decimal OffensiveOpportunityCost,
        string Confidence,
        bool ContainsGalacticLegend,
        int OmicronCount,
        int EligibleDatacronTier,
        int OpponentSamples,
        int PersonalSamples,
        IReadOnlyCollection<string> Reasons);

    private sealed record SmartSignals(
        IReadOnlyCollection<GacBattleAttackReserve> AttackReserves,
        IReadOnlyDictionary<string, decimal> CounterPreservationCost,
        IReadOnlySet<string> GalacticLegendUnitIds,
        IReadOnlyCollection<GacCounterPatternDetails> OpponentCounterPatterns,
        int OpponentRoundsAnalyzed,
        decimal? OpponentFullClearRate,
        IReadOnlyCollection<GacPersonalMatchupStatistics> PersonalStatistics,
        IReadOnlyCollection<GacPlannerDatacronDetails> Datacrons);
}
