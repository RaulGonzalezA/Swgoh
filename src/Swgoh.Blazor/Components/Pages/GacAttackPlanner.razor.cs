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
    private GacPlannerApiClient PlannerClient { get; set; } = null!;

    [Inject]
    private GacPlannerPerformanceApiClient PerformanceClient { get; set; } = null!;

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
        bool hasCachedContext = false;

        try
        {
            if (PerformanceClient.TryGetCachedContext(AllyCode, out GacPlannerPerformanceApiClient.PlannerContextViewModel? cached)
                && cached is not null)
            {
                ApplyContext(cached);
                hasCachedContext = true;
                Loading = false;
                await InvokeAsync(StateHasChanged);
            }

            GacPlannerPerformanceApiClient.PlannerContextResult result =
                await PerformanceClient.GetContextAsync(AllyCode);
            if (result.Context is not null)
            {
                ApplyContext(result.Context);
                UnavailableMessage = null;
            }
            else if (!hasCachedContext)
            {
                Planner = null;
                UnavailableMessage = result.Message;
            }
        }
        catch (HttpRequestException)
        {
            if (!hasCachedContext)
            {
                Error = "La API no está disponible en este momento.";
            }
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
}
