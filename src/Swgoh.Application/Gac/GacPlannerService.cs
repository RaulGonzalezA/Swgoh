using Swgoh.Application.Abstractions;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Gac;

internal sealed class GacPlannerService(
    IGacTeamPresetRepository presetRepository,
    IGacRoundPlanRepository planRepository,
    ICurrentGacScoutingService scoutingService,
    IPlayerProfileService playerProfileService,
    ISwgohGameDataCatalog gameDataCatalog,
    IClock clock) : IGacPlannerService
{
    public async Task<GacPlannerLookup> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        CurrentGacScoutingResult scouting = await scoutingService
            .GetAsync(allyCode, formatOverride: null, maxRounds: 30, cancellationToken)
            .ConfigureAwait(false);
        if (scouting.Lookup.Status != CurrentGacOpponentStatus.Found || scouting.Lookup.Opponent is null)
        {
            return new GacPlannerLookup(scouting.Lookup.Status, scouting.Lookup.Message, null);
        }

        GacPlannerState state = await BuildStateAsync(allyCode, scouting, cancellationToken).ConfigureAwait(false);
        return new GacPlannerLookup(CurrentGacOpponentStatus.Found, null, state);
    }

    public async Task<GacPlannerLookup> SaveCurrentAsync(
        long allyCode,
        SaveCurrentGacRoundPlan input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        CurrentGacScoutingResult scouting = await scoutingService
            .GetAsync(allyCode, formatOverride: null, maxRounds: 30, cancellationToken)
            .ConfigureAwait(false);
        if (scouting.Lookup.Status != CurrentGacOpponentStatus.Found || scouting.Lookup.Opponent is null)
        {
            return new GacPlannerLookup(scouting.Lookup.Status, scouting.Lookup.Message, null);
        }

        CurrentGacOpponent opponent = scouting.Lookup.Opponent;
        int roundNumber = ResolveRoundNumber(opponent);
        string planId = GacRoundPlan.BuildId(allyCode, opponent.EventInstanceId, roundNumber);
        GacRoundPlan plan = await planRepository.FindByIdAsync(planId, cancellationToken).ConfigureAwait(false)
            ?? GacRoundPlan.Create(
                allyCode,
                opponent.OpponentAllyCode,
                opponent.EventId,
                opponent.EventInstanceId,
                roundNumber,
                opponent.Format,
                opponent.League,
                clock.UtcNow);

        IReadOnlyCollection<GacTeamPreset> presets = await presetRepository
            .GetAsync(allyCode, opponent.Format, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, GacTeamPreset> presetsById = presets.ToDictionary(preset => preset.Id);

        GacOwnDefenseAssignment[] ownDefenses =
        [
            .. input.OwnDefenses.Select(item =>
            {
                ValidatePresetReference(item.TeamPresetId, presetsById, opponent.Format);
                return GacOwnDefenseAssignment.Create(item.Id ?? Guid.NewGuid(), item.Zone, item.TeamPresetId);
            })
        ];

        GacVisibleDefense[] visibleDefenses =
        [
            .. input.VisibleDefenses.Select(item => GacVisibleDefense.Create(
                item.Id ?? Guid.NewGuid(),
                item.Zone,
                item.Label,
                GacPlannerSquad.Create(
                    opponent.Format,
                    item.LeaderDefinitionId,
                    item.MemberDefinitionIds,
                    item.IsFleet)))
        ];

        GacAttackAssignment[] attacks =
        [
            .. input.Attacks.Select(item =>
            {
                ValidatePresetReference(item.TeamPresetId, presetsById, opponent.Format);
                return GacAttackAssignment.Create(
                    item.Id ?? Guid.NewGuid(),
                    item.DefenseId,
                    item.TeamPresetId,
                    item.Attempt,
                    item.Status,
                    item.Notes);
            })
        ];

        plan.Replace(ownDefenses, visibleDefenses, attacks, clock.UtcNow);
        await planRepository.UpsertAsync(plan, cancellationToken).ConfigureAwait(false);

        GacPlannerState state = await BuildStateAsync(allyCode, scouting, cancellationToken).ConfigureAwait(false);
        return new GacPlannerLookup(CurrentGacOpponentStatus.Found, null, state);
    }

    public async Task<GacTeamPresetDetails> CreatePresetAsync(
        long allyCode,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        PlayerProfile player = await GetRequiredPlayerAsync(allyCode, cancellationToken).ConfigureAwait(false);
        GacPlannerSquad squad = CreateAndValidateOwnedSquad(player, input);
        GacTeamPreset preset = GacTeamPreset.Create(
            Guid.NewGuid(),
            allyCode,
            input.Name,
            input.Format,
            input.Use,
            squad,
            clock.UtcNow);
        await presetRepository.UpsertAsync(preset, cancellationToken).ConfigureAwait(false);

        GameDataCatalog catalog = await gameDataCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
        return ToPresetDetails(preset, player, catalog);
    }

    public async Task<GacTeamPresetDetails?> UpdatePresetAsync(
        long allyCode,
        Guid id,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Team preset ID cannot be empty.", nameof(id));
        }

        GacTeamPreset? preset = await presetRepository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (preset is null || preset.AllyCode != allyCode)
        {
            return null;
        }

        PlayerProfile player = await GetRequiredPlayerAsync(allyCode, cancellationToken).ConfigureAwait(false);
        GacPlannerSquad squad = CreateAndValidateOwnedSquad(player, input);
        preset.Update(input.Name, input.Format, input.Use, squad, clock.UtcNow);
        await presetRepository.UpsertAsync(preset, cancellationToken).ConfigureAwait(false);

        GameDataCatalog catalog = await gameDataCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
        return ToPresetDetails(preset, player, catalog);
    }

    public async Task<bool> DeletePresetAsync(
        long allyCode,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Team preset ID cannot be empty.", nameof(id));
        }

        GacTeamPreset? preset = await presetRepository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (preset is null || preset.AllyCode != allyCode)
        {
            return false;
        }

        CurrentGacScoutingResult scouting = await scoutingService
            .GetAsync(allyCode, formatOverride: null, maxRounds: 30, cancellationToken)
            .ConfigureAwait(false);
        if (scouting.Lookup.Status == CurrentGacOpponentStatus.Found && scouting.Lookup.Opponent is not null)
        {
            CurrentGacOpponent opponent = scouting.Lookup.Opponent;
            string planId = GacRoundPlan.BuildId(allyCode, opponent.EventInstanceId, ResolveRoundNumber(opponent));
            GacRoundPlan? plan = await planRepository.FindByIdAsync(planId, cancellationToken).ConfigureAwait(false);
            if (plan is not null &&
                (plan.OwnDefenses.Any(item => item.TeamPresetId == id) ||
                 plan.Attacks.Any(item => item.TeamPresetId == id && item.Status != GacAttackPlanStatus.Cancelled)))
            {
                throw new ArgumentException(
                    "The team is used by the current round plan. Remove its assignments before deleting it.",
                    nameof(id));
            }
        }

        return await presetRepository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GacPlannerState> BuildStateAsync(
        long allyCode,
        CurrentGacScoutingResult scouting,
        CancellationToken cancellationToken)
    {
        CurrentGacOpponent opponent = scouting.Lookup.Opponent
            ?? throw new InvalidOperationException("A found GAC lookup must contain an opponent.");
        int roundNumber = ResolveRoundNumber(opponent);
        string planId = GacRoundPlan.BuildId(allyCode, opponent.EventInstanceId, roundNumber);

        Task<IReadOnlyCollection<GacTeamPreset>> presetsTask = presetRepository.GetAsync(
            allyCode,
            opponent.Format,
            cancellationToken);
        Task<GacRoundPlan?> planTask = planRepository.FindByIdAsync(planId, cancellationToken);
        Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(allyCode, cancellationToken);
        Task<PlayerProfile?> opponentTask = playerProfileService.GetAsync(opponent.OpponentAllyCode, cancellationToken);
        Task<GameDataCatalog> catalogTask = gameDataCatalog.GetAsync(cancellationToken);

        await Task.WhenAll(presetsTask, planTask, playerTask, opponentTask, catalogTask).ConfigureAwait(false);

        IReadOnlyCollection<GacTeamPreset> presets = await presetsTask.ConfigureAwait(false);
        PlayerProfile? player = await playerTask.ConfigureAwait(false);
        PlayerProfile? opponentPlayer = await opponentTask.ConfigureAwait(false);
        GameDataCatalog catalog = await catalogTask.ConfigureAwait(false);
        GacRoundPlan plan = await planTask.ConfigureAwait(false)
            ?? GacRoundPlan.Create(
                allyCode,
                opponent.OpponentAllyCode,
                opponent.EventId,
                opponent.EventInstanceId,
                roundNumber,
                opponent.Format,
                opponent.League,
                clock.UtcNow);

        Dictionary<Guid, GacTeamPreset> presetsById = presets.ToDictionary(preset => preset.Id);
        GacTeamPresetDetails[] presetDetails =
        [
            .. presets
                .OrderBy(preset => preset.Use)
                .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .Select(preset => ToPresetDetails(preset, player, catalog))
        ];
        Dictionary<Guid, GacTeamPresetDetails> detailsById = presetDetails.ToDictionary(preset => preset.Id);

        GacOwnDefenseAssignmentDetails[] ownDefenseDetails =
        [
            .. plan.OwnDefenses.Select(item => new GacOwnDefenseAssignmentDetails(
                item.Id,
                item.Zone,
                GetRequiredPresetDetails(item.TeamPresetId, detailsById)))
        ];
        GacVisibleDefenseDetails[] visibleDefenseDetails =
        [
            .. plan.VisibleDefenses.Select(defense => new GacVisibleDefenseDetails(
                defense.Id,
                defense.Zone,
                defense.Label,
                ToSquadDetails(defense.Squad, opponentPlayer, catalog),
                plan.Attacks.Any(attack =>
                    attack.DefenseId == defense.Id && attack.Status == GacAttackPlanStatus.Won)))
        ];
        GacAttackAssignmentDetails[] attackDetails =
        [
            .. plan.Attacks
                .OrderBy(attack => attack.DefenseId)
                .ThenBy(attack => attack.Attempt)
                .Select(attack => new GacAttackAssignmentDetails(
                    attack.Id,
                    attack.DefenseId,
                    GetRequiredPresetDetails(attack.TeamPresetId, detailsById),
                    attack.Attempt,
                    attack.Status,
                    attack.Notes))
        ];

        GacPlannerConflict[] conflicts = AnalyzeConflicts(plan, presetsById);
        GacPlannerCounterHint[] counterHints = BuildCounterHints(
            plan,
            scouting.BattlePlan,
            presets,
            player,
            catalog);
        GacRoundPlanDetails planDetails = new(
            plan.Id,
            plan.PlayerAllyCode,
            plan.OpponentAllyCode,
            opponent.OpponentName,
            plan.EventId,
            plan.EventInstanceId,
            plan.RoundNumber,
            plan.Format,
            plan.League,
            ownDefenseDetails,
            visibleDefenseDetails,
            attackDetails,
            conflicts,
            counterHints,
            plan.UpdatedAtUtc);

        return new GacPlannerState(opponent, presetDetails, planDetails);
    }

    private async Task<PlayerProfile> GetRequiredPlayerAsync(
        long allyCode,
        CancellationToken cancellationToken)
    {
        PlayerProfile? player = await playerProfileService.GetAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return player ?? throw new ArgumentException(
            "Player roster is not available. Refresh the player before creating teams.",
            nameof(allyCode));
    }

    private static GacPlannerSquad CreateAndValidateOwnedSquad(PlayerProfile player, SaveGacTeamPreset input)
    {
        GacPlannerSquad squad = GacPlannerSquad.Create(
            input.Format,
            input.LeaderDefinitionId,
            input.MemberDefinitionIds,
            input.IsFleet);
        Dictionary<string, RosterUnit> roster = player.Roster.ToDictionary(
            unit => unit.DefinitionId,
            StringComparer.OrdinalIgnoreCase);

        foreach (string definitionId in squad.AllUnitDefinitionIds)
        {
            if (!roster.TryGetValue(definitionId, out RosterUnit? unit))
            {
                throw new ArgumentException(
                    $"Unit '{definitionId}' is not available in the player's roster.",
                    nameof(input));
            }

            if (unit.IsShip != squad.IsFleet)
            {
                throw new ArgumentException(
                    $"Unit '{definitionId}' does not match the selected team type.",
                    nameof(input));
            }
        }

        return squad;
    }

    private static void ValidatePresetReference(
        Guid presetId,
        IReadOnlyDictionary<Guid, GacTeamPreset> presets,
        GacFormat format)
    {
        if (!presets.TryGetValue(presetId, out GacTeamPreset? preset))
        {
            throw new ArgumentException($"Team preset '{presetId}' is not available for this player.");
        }

        if (preset.Format != format)
        {
            throw new ArgumentException($"Team preset '{preset.Name}' does not match the current GAC format.");
        }
    }

    private static GacTeamPresetDetails GetRequiredPresetDetails(
        Guid id,
        IReadOnlyDictionary<Guid, GacTeamPresetDetails> presets)
    {
        return presets.TryGetValue(id, out GacTeamPresetDetails? details)
            ? details
            : throw new InvalidOperationException($"Round plan references missing team preset '{id}'.");
    }

    private static GacTeamPresetDetails ToPresetDetails(
        GacTeamPreset preset,
        PlayerProfile? player,
        GameDataCatalog catalog) => new(
            preset.Id,
            preset.AllyCode,
            preset.Name,
            preset.Format,
            preset.Use,
            ToSquadDetails(preset.Squad, player, catalog),
            preset.UpdatedAtUtc);

    private static GacPlannerSquadDetails ToSquadDetails(
        GacPlannerSquad squad,
        PlayerProfile? player,
        GameDataCatalog catalog) => new(
            ToUnitDetails(squad.LeaderDefinitionId, player, catalog),
            [.. squad.MemberDefinitionIds.Select(id => ToUnitDetails(id, player, catalog))],
            squad.IsFleet);

    private static GacPlannerUnitDetails ToUnitDetails(
        string definitionId,
        PlayerProfile? player,
        GameDataCatalog catalog)
    {
        catalog.Units.TryGetValue(definitionId, out GameUnitDefinition? definition);
        RosterUnit? rosterUnit = player?.Roster.FirstOrDefault(unit =>
            string.Equals(unit.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase));
        return new GacPlannerUnitDetails(
            definitionId,
            definition?.Name ?? definitionId,
            definition?.ThumbnailName,
            definition?.IsShip ?? rosterUnit?.IsShip ?? false,
            rosterUnit?.GalacticPower,
            rosterUnit?.RelicTier,
            rosterUnit?.ZetaCount,
            rosterUnit?.OmicronCount);
    }

    private static GacPlannerConflict[] AnalyzeConflicts(
        GacRoundPlan plan,
        IReadOnlyDictionary<Guid, GacTeamPreset> presets)
    {
        var conflicts = new List<GacPlannerConflict>();
        var defenseUsages = plan.OwnDefenses
            .Where(item => presets.ContainsKey(item.TeamPresetId))
            .Select(item => new TeamUsage(item.Id, "defense", presets[item.TeamPresetId]))
            .ToArray();
        var attackUsages = plan.Attacks
            .Where(item => item.Status != GacAttackPlanStatus.Cancelled && presets.ContainsKey(item.TeamPresetId))
            .Select(item => new TeamUsage(item.Id, "attack", presets[item.TeamPresetId]))
            .ToArray();

        AddPairConflicts(
            defenseUsages,
            defenseUsages,
            sameCollection: true,
            "OwnDefenseOverlap",
            "Error",
            "Two of your defensive teams share units.",
            conflicts);
        AddPairConflicts(
            attackUsages,
            attackUsages,
            sameCollection: true,
            "AttackReuse",
            "Error",
            "Two planned or already used attacks share units.",
            conflicts);
        AddPairConflicts(
            defenseUsages,
            attackUsages,
            sameCollection: false,
            "DefenseAttackOverlap",
            "Error",
            "An attack uses units that are already placed on your defense.",
            conflicts);

        foreach (GacVisibleDefense defense in plan.VisibleDefenses)
        {
            bool won = plan.Attacks.Any(attack =>
                attack.DefenseId == defense.Id && attack.Status == GacAttackPlanStatus.Won);
            GacAttackAssignment[] pendingAfterWin =
            [
                .. plan.Attacks.Where(attack =>
                    attack.DefenseId == defense.Id &&
                    attack.Status == GacAttackPlanStatus.Planned &&
                    won)
            ];
            if (pendingAfterWin.Length > 0)
            {
                conflicts.Add(new GacPlannerConflict(
                    "AttackAfterWin",
                    "Warning",
                    "A defeated enemy team still has a planned attack assigned.",
                    [.. pendingAfterWin.Select(attack => attack.Id)],
                    []));
            }
        }

        return [.. conflicts];
    }

    private static void AddPairConflicts(
        IReadOnlyList<TeamUsage> left,
        IReadOnlyList<TeamUsage> right,
        bool sameCollection,
        string code,
        string severity,
        string message,
        ICollection<GacPlannerConflict> conflicts)
    {
        for (int leftIndex = 0; leftIndex < left.Count; leftIndex++)
        {
            int rightStart = sameCollection ? leftIndex + 1 : 0;
            for (int rightIndex = rightStart; rightIndex < right.Count; rightIndex++)
            {
                TeamUsage first = left[leftIndex];
                TeamUsage second = right[rightIndex];
                string[] overlap =
                [
                    .. first.Preset.Squad.AllUnitDefinitionIds.Intersect(
                        second.Preset.Squad.AllUnitDefinitionIds,
                        StringComparer.OrdinalIgnoreCase)
                ];
                if (overlap.Length == 0)
                {
                    continue;
                }

                conflicts.Add(new GacPlannerConflict(
                    code,
                    severity,
                    $"{message} {first.Preset.Name} ↔ {second.Preset.Name}.",
                    [first.AssignmentId, second.AssignmentId],
                    overlap));
            }
        }
    }

    private static GacPlannerCounterHint[] BuildCounterHints(
        GacRoundPlan plan,
        CurrentGacBattlePlan? battlePlan,
        IReadOnlyCollection<GacTeamPreset> presets,
        PlayerProfile? player,
        GameDataCatalog catalog)
    {
        if (battlePlan is null)
        {
            return [];
        }

        var hints = new List<GacPlannerCounterHint>();
        foreach (GacVisibleDefense defense in plan.VisibleDefenses)
        {
            GacBattleCounterSuggestion? suggestion = battlePlan.CounterSuggestions.FirstOrDefault(counter =>
                string.Equals(
                    counter.Threat.DefinitionId,
                    defense.Squad.LeaderDefinitionId,
                    StringComparison.OrdinalIgnoreCase));
            if (suggestion is null)
            {
                continue;
            }

            IReadOnlyCollection<GacBattleUnit> recommended = suggestion.RecommendedTeam is { Count: > 0 }
                ? suggestion.RecommendedTeam
                : suggestion.CandidateAnchors;
            string[] recommendedIds = [.. recommended.Select(unit => unit.DefinitionId)];
            GacTeamPreset? matchingPreset = FindMatchingPreset(presets, recommendedIds, defense.Squad.IsFleet);
            hints.Add(new GacPlannerCounterHint(
                defense.Id,
                suggestion.Threat.Name,
                suggestion.Confidence,
                suggestion.Source,
                suggestion.Rationale,
                suggestion.RequiresDatacronVerification,
                matchingPreset?.Id,
                [.. recommended.Select(unit => ToUnitDetails(unit.DefinitionId, player, catalog))],
                suggestion.Uses,
                suggestion.WinRate,
                suggestion.OneShotRate,
                suggestion.AverageBanners,
                suggestion.PlayersObserved));
        }

        return [.. hints];
    }

    private static GacTeamPreset? FindMatchingPreset(
        IReadOnlyCollection<GacTeamPreset> presets,
        IReadOnlyCollection<string> recommendedIds,
        bool isFleet)
    {
        if (recommendedIds.Count == 0)
        {
            return null;
        }

        HashSet<string> recommended = recommendedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return presets
            .Where(preset => preset.Squad.IsFleet == isFleet && preset.Use != GacPlannerTeamUse.Defense)
            .OrderBy(preset => preset.Use == GacPlannerTeamUse.Offense ? 0 : 1)
            .FirstOrDefault(preset =>
            {
                HashSet<string> units = preset.Squad.AllUnitDefinitionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                return recommended.Count == units.Count
                    ? recommended.SetEquals(units)
                    : recommended.All(units.Contains);
            });
    }

    private static int ResolveRoundNumber(CurrentGacOpponent opponent) => opponent.RoundNumber.GetValueOrDefault(1);

    private sealed record TeamUsage(Guid AssignmentId, string Kind, GacTeamPreset Preset);
}
