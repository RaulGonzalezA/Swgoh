using Microsoft.AspNetCore.Components;

using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class GacAttackPlanner
{
    private const string AssetBaseUrl = "https://game-assets.swgoh.gg/textures/";

    private readonly List<PlayerApiClient.RosterUnitViewModel> playerRoster = [];
    private readonly List<PlayerApiClient.RosterUnitViewModel> opponentRoster = [];
    private readonly List<OwnDefenseDraft> ownDefenses = [];
    private readonly List<VisibleDefenseDraft> visibleDefenses = [];
    private readonly List<AttackDraft> attacks = [];
    private readonly Dictionary<Guid, string> selectedAttackPresets = [];
    private readonly string[] presetMemberIds = new string[7];
    private readonly string[] enemyMemberIds = new string[7];

    [Inject]
    private PlayerApiClient PlayerClient { get; set; } = null!;

    [Inject]
    private GacPlannerApiClient PlannerClient { get; set; } = null!;

    [Parameter]
    public long AllyCode { get; set; }

    protected GacPlannerApiClient.PlannerViewModel? Planner { get; private set; }
    protected bool Loading { get; private set; } = true;
    protected bool Saving { get; private set; }
    protected string? Error { get; private set; }
    protected string? UnavailableMessage { get; private set; }
    protected bool ShowPresetBuilder { get; private set; }
    protected bool ShowEnemyBuilder { get; private set; }
    protected string PresetName { get; set; } = string.Empty;
    protected string PresetUse { get; set; } = "Offense";
    protected string PresetType { get; private set; } = "Character";
    protected string PresetLeaderId { get; set; } = string.Empty;
    protected string OwnDefenseZone { get; set; } = "Sur frontal";
    protected string OwnDefensePresetId { get; set; } = string.Empty;
    protected string EnemyZone { get; set; } = "Sur frontal";
    protected string EnemyLabel { get; set; } = string.Empty;
    protected string EnemyType { get; private set; } = "Character";
    protected string EnemyLeaderId { get; set; } = string.Empty;
    protected string[] PresetMemberIds => presetMemberIds;
    protected string[] EnemyMemberIds => enemyMemberIds;
    protected List<OwnDefenseDraft> OwnDefenses => ownDefenses;
    protected List<VisibleDefenseDraft> VisibleDefenses => visibleDefenses;

    protected static IReadOnlyList<string> ZoneOptions { get; } =
    [
        "Sur frontal",
        "Norte frontal",
        "Sur trasera",
        "Norte trasera",
        "Flota"
    ];

    private string presetFilter = "All";

    protected int CharacterTeamSize => Planner?.Opponent.Format == "3v3" ? 3 : 5;
    protected int PresetMemberSlotCount => PresetType == "Fleet" ? 7 : CharacterTeamSize - 1;
    protected int EnemyMemberSlotCount => EnemyType == "Fleet" ? 7 : CharacterTeamSize - 1;
    protected bool PresetMembersRequired => PresetType != "Fleet";
    protected bool EnemyMembersRequired => EnemyType != "Fleet";

    protected int PlannedCoverage => visibleDefenses.Count(defense => attacks.Any(attack =>
        attack.DefenseId == defense.Id && attack.Status is "Planned" or "Won"));

    protected int ActiveAttacks => attacks.Count(attack => attack.Status != "Cancelled");
    protected int CompletedWins => attacks.Count(attack => attack.Status == "Won");
    protected bool HasBlockingConflicts => Planner?.Plan.Conflicts.Any(conflict => conflict.Severity == "Error") == true;

    protected string AllPresetFilterClass => PresetFilterClass("All");
    protected string DefensePresetFilterClass => PresetFilterClass("Defense");
    protected string OffensePresetFilterClass => PresetFilterClass("Offense");

    protected IReadOnlyCollection<GacPlannerApiClient.TeamPresetViewModel> FilteredPresets => Planner is null
        ? []
        : [.. Planner.Presets
            .Where(preset => presetFilter == "All" || preset.Use == presetFilter)
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)];

    protected IReadOnlyCollection<GacPlannerApiClient.TeamPresetViewModel> DefensePresetOptions => Planner is null
        ? []
        : [.. Planner.Presets
            .Where(preset => ownDefenses.All(assignment => assignment.TeamPresetId != preset.Id))
            .OrderBy(preset => preset.Use == "Defense" ? 0 : preset.Use == "Flexible" ? 1 : 2)
            .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)];

    protected IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> PresetUnitOptions =>
        [.. playerRoster
            .Where(unit => unit.IsShip == (PresetType == "Fleet"))
            .OrderByDescending(unit => unit.GalacticPower)
            .ThenBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)];

    protected IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> EnemyUnitOptions =>
        [.. opponentRoster
            .Where(unit => unit.IsShip == (EnemyType == "Fleet"))
            .OrderByDescending(unit => unit.GalacticPower)
            .ThenBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)];

    protected bool CanCreatePreset =>
        !string.IsNullOrWhiteSpace(PresetName) &&
        !string.IsNullOrWhiteSpace(PresetLeaderId) &&
        HasEnoughMembers(presetMemberIds, PresetMemberSlotCount, PresetMembersRequired);

    protected bool CanAddVisibleDefense =>
        !string.IsNullOrWhiteSpace(EnemyLeaderId) &&
        HasEnoughMembers(enemyMemberIds, EnemyMemberSlotCount, EnemyMembersRequired);

    protected override async Task OnParametersSetAsync()
    {
        Loading = true;
        Error = null;
        UnavailableMessage = null;

        try
        {
            GacPlannerApiClient.PlannerResult result = await PlannerClient.GetCurrentAsync(AllyCode);
            Planner = result.Planner;
            UnavailableMessage = result.Message;
            if (Planner is null)
            {
                return;
            }

            MapDraftsFromPlanner();
            IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> ownRoster = await LoadEntireRosterAsync(AllyCode);
            IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> rivalRoster =
                await LoadEntireRosterAsync(Planner.Opponent.OpponentAllyCode);

            playerRoster.Clear();
            playerRoster.AddRange(ownRoster);
            opponentRoster.Clear();
            opponentRoster.AddRange(rivalRoster);
        }
        catch (HttpRequestException)
        {
            Error = "La API no está disponible en este momento.";
        }
        finally
        {
            Loading = false;
        }
    }

    protected void TogglePresetBuilder() => ShowPresetBuilder = !ShowPresetBuilder;

    protected void ToggleEnemyBuilder() => ShowEnemyBuilder = !ShowEnemyBuilder;

    protected void ShowAllPresets() => presetFilter = "All";

    protected void ShowDefensePresets() => presetFilter = "Defense";

    protected void ShowOffensePresets() => presetFilter = "Offense";

    protected void PresetTypeChanged(ChangeEventArgs eventArgs)
    {
        PresetType = eventArgs.Value?.ToString() == "Fleet" ? "Fleet" : "Character";
        PresetLeaderId = string.Empty;
        Array.Fill(presetMemberIds, string.Empty);
    }

    protected void EnemyTypeChanged(ChangeEventArgs eventArgs)
    {
        EnemyType = eventArgs.Value?.ToString() == "Fleet" ? "Fleet" : "Character";
        EnemyLeaderId = string.Empty;
        Array.Fill(enemyMemberIds, string.Empty);
        EnemyZone = EnemyType == "Fleet" ? "Flota" : "Sur frontal";
    }

    protected void SetPresetMember(int index, string? value) => presetMemberIds[index] = value ?? string.Empty;

    protected void SetEnemyMember(int index, string? value) => enemyMemberIds[index] = value ?? string.Empty;

    protected async Task CreatePresetAsync()
    {
        if (Planner is null || !CanCreatePreset)
        {
            return;
        }

        Saving = true;
        Error = null;
        try
        {
            IReadOnlyCollection<string> members = SelectedMembers(
                presetMemberIds,
                PresetMemberSlotCount,
                PresetMembersRequired);
            await PlannerClient.CreatePresetAsync(
                AllyCode,
                new GacPlannerApiClient.SavePresetRequest(
                    PresetName.Trim(),
                    Planner.Opponent.Format,
                    PresetUse,
                    PresetLeaderId,
                    members,
                    PresetType == "Fleet"));
            await ReloadPlannerAsync();
            ResetPresetBuilder();
        }
        catch (HttpRequestException)
        {
            Error = "No se ha podido guardar el equipo. Comprueba que no repites unidades y que todas pertenecen a tu roster.";
        }
        finally
        {
            Saving = false;
        }
    }

    protected async Task DeletePresetAsync(Guid id)
    {
        Saving = true;
        Error = null;
        try
        {
            await PlannerClient.DeletePresetAsync(AllyCode, id);
            await ReloadPlannerAsync();
        }
        catch (HttpRequestException)
        {
            Error = "Ese equipo está siendo usado en el plan actual. Quita primero sus asignaciones.";
        }
        finally
        {
            Saving = false;
        }
    }

    protected async Task AddOwnDefenseAsync()
    {
        if (!Guid.TryParse(OwnDefensePresetId, out Guid presetId))
        {
            Error = "Selecciona un equipo antes de añadirlo a tu defensa.";
            return;
        }

        if (ownDefenses.Any(item => item.TeamPresetId == presetId))
        {
            Error = "Ese equipo ya está colocado en tu defensa.";
            return;
        }

        string selectedPresetId = OwnDefensePresetId;
        var draft = new OwnDefenseDraft(Guid.Empty, OwnDefenseZone, presetId);
        ownDefenses.Add(draft);

        if (await SavePlanAsync())
        {
            OwnDefensePresetId = string.Empty;
            return;
        }

        ownDefenses.Remove(draft);
        OwnDefensePresetId = selectedPresetId;
    }

    protected async Task RemoveOwnDefenseAsync(Guid id)
    {
        ownDefenses.RemoveAll(item => item.Id == id);
        await SavePlanAsync();
    }

    protected async Task AddVisibleDefenseAsync()
    {
        if (!CanAddVisibleDefense)
        {
            return;
        }

        IReadOnlyCollection<string> members = SelectedMembers(
            enemyMemberIds,
            EnemyMemberSlotCount,
            EnemyMembersRequired);
        visibleDefenses.Add(new VisibleDefenseDraft(
            Guid.Empty,
            EnemyZone,
            string.IsNullOrWhiteSpace(EnemyLabel) ? null : EnemyLabel.Trim(),
            EnemyLeaderId,
            members,
            EnemyType == "Fleet"));

        await SavePlanAsync();
        ResetEnemyBuilder();
    }

    protected async Task RemoveVisibleDefenseAsync(Guid id)
    {
        visibleDefenses.RemoveAll(item => item.Id == id);
        attacks.RemoveAll(item => item.DefenseId == id);
        selectedAttackPresets.Remove(id);
        await SavePlanAsync();
    }

    protected void SetAttackPreset(Guid defenseId, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            selectedAttackPresets.Remove(defenseId);
        }
        else
        {
            selectedAttackPresets[defenseId] = value;
        }
    }

    protected string SelectedAttackPreset(Guid defenseId) =>
        selectedAttackPresets.TryGetValue(defenseId, out string? value) ? value : string.Empty;

    protected async Task PlanSelectedAttackAsync(Guid defenseId)
    {
        if (!selectedAttackPresets.TryGetValue(defenseId, out string? selected) ||
            !Guid.TryParse(selected, out Guid presetId))
        {
            return;
        }

        await PlanAttackAsync(defenseId, presetId);
    }

    protected async Task PlanAttackAsync(Guid defenseId, Guid presetId)
    {
        attacks.Add(new AttackDraft(
            Guid.Empty,
            defenseId,
            presetId,
            NextAttempt(defenseId),
            "Planned",
            null));
        selectedAttackPresets.Remove(defenseId);
        await SavePlanAsync();
    }

    protected Task MarkAttackWonAsync(Guid attackId) => SetAttackStatusAsync(attackId, "Won");

    protected Task MarkAttackFailedAsync(Guid attackId) => SetAttackStatusAsync(attackId, "Failed");

    protected Task CancelAttackAsync(Guid attackId) => SetAttackStatusAsync(attackId, "Cancelled");

    protected IReadOnlyCollection<GacPlannerApiClient.TeamPresetViewModel> AttackPresetOptions(bool isFleet) => Planner is null
        ? []
        : [.. Planner.Presets
            .Where(preset => preset.Squad.IsFleet == isFleet)
            .OrderBy(preset => preset.Use == "Offense" ? 0 : preset.Use == "Flexible" ? 1 : 2)
            .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)];

    protected GacPlannerApiClient.TeamPresetViewModel? FindPreset(Guid id) =>
        Planner?.Presets.FirstOrDefault(preset => preset.Id == id);

    protected GacPlannerApiClient.VisibleDefenseViewModel? FindVisibleDefense(Guid id) =>
        Planner?.Plan.VisibleDefenses.FirstOrDefault(defense => defense.Id == id);

    protected GacPlannerApiClient.CounterHintViewModel? FindCounterHint(Guid id) =>
        Planner?.Plan.CounterHints.FirstOrDefault(hint => hint.DefenseId == id);

    protected IReadOnlyCollection<AttackDraft> AttacksFor(Guid defenseId) =>
        [.. attacks.Where(attack => attack.DefenseId == defenseId)];

    protected int NextAttempt(Guid defenseId) => attacks
        .Where(attack => attack.DefenseId == defenseId)
        .Select(attack => attack.Attempt)
        .DefaultIfEmpty(0)
        .Max() + 1;

    protected string EnemyLeaderName(
        VisibleDefenseDraft defense,
        GacPlannerApiClient.VisibleDefenseViewModel? details) =>
        details?.Squad.Leader.Name ?? UnitName(opponentRoster, defense.LeaderDefinitionId);

    protected static string ConflictLabel(string severity) => severity == "Error" ? "Bloqueo" : "Aviso";

    protected static string UnitOptionLabel(PlayerApiClient.RosterUnitViewModel unit) => unit.IsShip
        ? $"{unit.Name} · {unit.GalacticPower:N0} GP"
        : $"{unit.Name} · {ProgressLabel(unit)} · {unit.GalacticPower:N0} GP";

    protected static string SquadNames(GacPlannerApiClient.PlannerSquadViewModel squad) =>
        string.Join(" · ", squad.AllUnits.Select(unit => unit.Name));

    protected static string FriendlyUse(string use) => use switch
    {
        "Defense" => "Defensa",
        "Offense" => "Ataque",
        "Flexible" => "Flexible",
        _ => use
    };

    protected static string FriendlyAttackStatus(string status) => status switch
    {
        "Planned" => "Planificado",
        "Won" => "Victoria",
        "Failed" => "Fallo",
        "Cancelled" => "Cancelado",
        _ => status
    };

    protected static string FriendlyConfidence(string confidence) => confidence switch
    {
        "High" => "Alta",
        "Medium" => "Media",
        "Low" => "Baja",
        _ => confidence
    };

    protected static string Percent(decimal value)
    {
        decimal normalized = value <= 1 ? value * 100 : value;
        return $"{Math.Clamp(normalized, 0, 100):0.#}%";
    }

    protected static string? GetPortraitUrl(string? thumbnailName)
    {
        if (string.IsNullOrWhiteSpace(thumbnailName))
        {
            return null;
        }

        string assetName = thumbnailName.Trim();
        if (!assetName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            assetName += ".png";
        }

        return AssetBaseUrl + Uri.EscapeDataString(assetName);
    }

    protected static string Initial(string name) => string.IsNullOrWhiteSpace(name)
        ? "?"
        : char.ToUpperInvariant(name.Trim()[0]).ToString();

    private async Task<IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel>> LoadEntireRosterAsync(long allyCode)
    {
        var units = new List<PlayerApiClient.RosterUnitViewModel>();
        int page = 1;
        int totalPages;
        do
        {
            PlayerApiClient.RosterPageViewModel? result = await PlayerClient.GetRosterAsync(
                allyCode,
                page,
                pageSize: 100,
                orderBy: "GalacticPower",
                direction: "Descending");
            if (result is null)
            {
                break;
            }

            units.AddRange(result.Items);
            totalPages = result.TotalPages;
            page++;
        }
        while (page <= totalPages);

        return units;
    }

    private async Task<bool> SavePlanAsync()
    {
        if (Planner is null)
        {
            Error = "No hay una ronda de Gran Arena disponible para guardar el plan.";
            return false;
        }

        Saving = true;
        Error = null;
        try
        {
            var request = new GacPlannerApiClient.SavePlanRequest(
                [.. ownDefenses.Select(item => new GacPlannerApiClient.SaveOwnDefenseRequest(
                    NullWhenEmpty(item.Id),
                    item.Zone,
                    item.TeamPresetId))],
                [.. visibleDefenses.Select(item => new GacPlannerApiClient.SaveVisibleDefenseRequest(
                    NullWhenEmpty(item.Id),
                    item.Zone,
                    item.Label,
                    item.LeaderDefinitionId,
                    item.MemberDefinitionIds,
                    item.IsFleet))],
                [.. attacks.Select(item => new GacPlannerApiClient.SaveAttackRequest(
                    NullWhenEmpty(item.Id),
                    item.DefenseId,
                    item.TeamPresetId,
                    item.Attempt,
                    item.Status,
                    item.Notes))]);

            GacPlannerApiClient.PlannerResult result = await PlannerClient.SaveCurrentAsync(AllyCode, request);
            if (result.Planner is null)
            {
                Error = result.Message ?? "No se ha podido guardar el plan de la ronda.";
                UnavailableMessage = result.Message;
                return false;
            }

            Planner = result.Planner;
            UnavailableMessage = result.Message;
            MapDraftsFromPlanner();
            return true;
        }
        catch (HttpRequestException)
        {
            Error = "No se ha podido guardar el plan. Revisa que los equipos tengan el tamaño correcto y no repitan unidades.";
            return false;
        }
        finally
        {
            Saving = false;
        }
    }

    private async Task ReloadPlannerAsync()
    {
        GacPlannerApiClient.PlannerResult result = await PlannerClient.GetCurrentAsync(AllyCode);
        Planner = result.Planner;
        UnavailableMessage = result.Message;
        if (Planner is not null)
        {
            MapDraftsFromPlanner();
        }
    }

    private async Task SetAttackStatusAsync(Guid attackId, string status)
    {
        int index = attacks.FindIndex(item => item.Id == attackId);
        if (index < 0)
        {
            return;
        }

        attacks[index] = attacks[index] with { Status = status };
        await SavePlanAsync();
    }

    private void MapDraftsFromPlanner()
    {
        if (Planner is null)
        {
            return;
        }

        ownDefenses.Clear();
        ownDefenses.AddRange(Planner.Plan.OwnDefenses.Select(item =>
            new OwnDefenseDraft(item.Id, item.Zone, item.Team.Id)));

        visibleDefenses.Clear();
        visibleDefenses.AddRange(Planner.Plan.VisibleDefenses.Select(item =>
            new VisibleDefenseDraft(
                item.Id,
                item.Zone,
                item.Label,
                item.Squad.Leader.DefinitionId,
                [.. item.Squad.Members.Select(unit => unit.DefinitionId)],
                item.Squad.IsFleet)));

        attacks.Clear();
        attacks.AddRange(Planner.Plan.Attacks.Select(item =>
            new AttackDraft(
                item.Id,
                item.DefenseId,
                item.Team.Id,
                item.Attempt,
                item.Status,
                item.Notes)));
    }

    private void ResetPresetBuilder()
    {
        PresetName = string.Empty;
        PresetUse = "Offense";
        PresetLeaderId = string.Empty;
        Array.Fill(presetMemberIds, string.Empty);
        ShowPresetBuilder = false;
    }

    private void ResetEnemyBuilder()
    {
        EnemyLabel = string.Empty;
        EnemyLeaderId = string.Empty;
        Array.Fill(enemyMemberIds, string.Empty);
        ShowEnemyBuilder = false;
    }

    private string PresetFilterClass(string value) => presetFilter == value ? "active" : string.Empty;

    private static bool HasEnoughMembers(string[] source, int slotCount, bool allRequired)
    {
        int selectedCount = source.Take(slotCount).Count(value => !string.IsNullOrWhiteSpace(value));
        return allRequired ? selectedCount == slotCount : selectedCount >= 1;
    }

    private static IReadOnlyCollection<string> SelectedMembers(string[] source, int count, bool allRequired)
    {
        string[] selected = [.. source.Take(count).Where(value => !string.IsNullOrWhiteSpace(value))];
        return allRequired && selected.Length != count ? [] : selected;
    }

    private static Guid? NullWhenEmpty(Guid id) => id == Guid.Empty ? null : id;

    private static string ProgressLabel(PlayerApiClient.RosterUnitViewModel unit) =>
        unit.RelicTier > 0 ? $"R{unit.RelicTier}" : $"G{unit.GearTier}";

    private static string UnitName(
        IEnumerable<PlayerApiClient.RosterUnitViewModel> roster,
        string definitionId) => roster.FirstOrDefault(unit =>
            string.Equals(unit.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))?.Name ?? definitionId;

    protected sealed record OwnDefenseDraft(Guid Id, string Zone, Guid TeamPresetId);

    protected sealed record VisibleDefenseDraft(
        Guid Id,
        string Zone,
        string? Label,
        string LeaderDefinitionId,
        IReadOnlyCollection<string> MemberDefinitionIds,
        bool IsFleet);

    protected sealed record AttackDraft(
        Guid Id,
        Guid DefenseId,
        Guid TeamPresetId,
        int Attempt,
        string Status,
        string? Notes);
}
