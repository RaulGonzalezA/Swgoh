using Microsoft.AspNetCore.Components;

using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class GacExecutionPanel
{
    [Parameter, EditorRequired]
    public long AllyCode { get; set; }

    [Parameter, EditorRequired]
    public GacPlannerApiClient.PlannerViewModel Planner { get; set; } = null!;

    [Parameter]
    public GacPlannerApiClient.OptimizationViewModel? Optimization { get; set; }

    [Parameter]
    public IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> OwnRoster { get; set; } = [];

    [Parameter]
    public IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> OpponentRoster { get; set; } = [];

    [Parameter]
    public EventCallback<GacAttackExecutionApiClient.ExecutionEnvelopeViewModel> Executed { get; set; }

    private Guid? SelectedAttackId { get; set; }
    private string ExecutionStatus { get; set; } = "Won";
    private int? ExecutionBanners { get; set; }
    private string ExecutionNotes { get; set; } = string.Empty;
    private bool ExecutionPreloadedTurnMeter { get; set; }
    private HashSet<string> RemainingEnemyUnits { get; } = new(StringComparer.OrdinalIgnoreCase);
    private bool ExecutionBusy { get; set; }
    private string? ExecutionError { get; set; }
    private GacAttackExecutionApiClient.ExecutionEnvelopeViewModel? LastExecution { get; set; }

    private IReadOnlyCollection<GacPlannerApiClient.AttackViewModel> PlannedAttacks =>
        [.. Planner.Plan.Attacks
            .Where(attack => attack.Status == "Planned")
            .OrderBy(WarRoomOrder)
            .ThenBy(attack => DefenseZone(attack.DefenseId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(attack => attack.Attempt)];

    private IReadOnlyCollection<GacPlannerApiClient.AttackViewModel> ReservedAfterNext
    {
        get
        {
            if (LastExecution?.NextRecommendation is not { } next)
            {
                return [];
            }

            return
            [
                .. PlannedAttacks
                    .Where(attack =>
                        attack.DefenseId != next.DefenseId ||
                        attack.Team.Id != next.TeamPresetId)
                    .Take(2)
            ];
        }
    }

    private IReadOnlyCollection<GacPlannerApiClient.AttackViewModel> CompletedAttacks =>
        [.. Planner.Plan.Attacks
            .Where(attack => attack.Status is "Won" or "Failed")
            .OrderByDescending(attack => attack.Attempt)];

    private int WonAttacks => Planner.Plan.Attacks.Count(attack => attack.Status == "Won");
    private int FailedAttacks => Planner.Plan.Attacks.Count(attack => attack.Status == "Failed");

    private decimal? KnownBannerAverage
    {
        get
        {
            int[] banners =
            [
                .. Planner.Plan.Attacks
                    .Where(attack => attack.Status == "Won" && attack.Banners is not null)
                    .Select(attack => attack.Banners!.Value)
            ];
            return banners.Length == 0 ? null : Math.Round((decimal)banners.Average(), 1);
        }
    }

    private GacPlannerApiClient.AttackViewModel? SelectedAttack => SelectedAttackId is Guid id
        ? Planner.Plan.Attacks.FirstOrDefault(attack => attack.Id == id)
        : null;

    protected override void OnParametersSet()
    {
        if (SelectedAttackId is Guid id && Planner.Plan.Attacks.All(attack => attack.Id != id || attack.Status != "Planned"))
        {
            CloseEditor();
        }
    }

    private void OpenEditor(Guid attackId)
    {
        GacPlannerApiClient.AttackViewModel? attack = Planner.Plan.Attacks.FirstOrDefault(item => item.Id == attackId);
        if (attack is null || attack.Status != "Planned")
        {
            return;
        }

        SelectedAttackId = attackId;
        ExecutionStatus = "Won";
        ExecutionBanners = attack.Banners;
        ExecutionNotes = string.Empty;
        ExecutionPreloadedTurnMeter = false;
        InitializeSurvivors(attack.DefenseId);
        ExecutionError = null;
    }

    private void CloseEditor()
    {
        SelectedAttackId = null;
        ExecutionStatus = "Won";
        ExecutionBanners = null;
        ExecutionNotes = string.Empty;
        ExecutionPreloadedTurnMeter = false;
        RemainingEnemyUnits.Clear();
        ExecutionError = null;
    }

    private void SelectWon()
    {
        ExecutionStatus = "Won";
        ExecutionPreloadedTurnMeter = false;
    }

    private void SelectFailed()
    {
        ExecutionStatus = "Failed";
        if (SelectedAttack is { } attack && RemainingEnemyUnits.Count == 0)
        {
            InitializeSurvivors(attack.DefenseId);
        }
    }

    private void InitializeSurvivors(Guid defenseId)
    {
        RemainingEnemyUnits.Clear();
        GacPlannerApiClient.VisibleDefenseViewModel? defense = Planner.Plan.VisibleDefenses.FirstOrDefault(item => item.Id == defenseId);
        if (defense is null)
        {
            return;
        }

        foreach (GacPlannerApiClient.PlannerUnitViewModel unit in defense.Squad.AllUnits)
        {
            RemainingEnemyUnits.Add(unit.DefinitionId);
        }
    }

    private void ToggleSurvivor(string definitionId)
    {
        if (!RemainingEnemyUnits.Remove(definitionId))
        {
            RemainingEnemyUnits.Add(definitionId);
        }
    }

    private async Task SubmitExecutionAsync()
    {
        if (SelectedAttackId is not Guid attackId)
        {
            return;
        }

        if (ExecutionBanners is < 0 or > 100)
        {
            ExecutionError = "Los banners deben estar entre 0 y 100.";
            return;
        }

        if (ExecutionStatus == "Failed" && RemainingEnemyUnits.Count == 0)
        {
            ExecutionError = "Si el ataque falla debe quedar al menos un enemigo vivo. Si no queda ninguno, registra la batalla como victoria.";
            return;
        }

        string[]? survivors = ExecutionStatus == "Failed" ? [.. RemainingEnemyUnits] : null;
        ExecutionBusy = true;
        ExecutionError = null;
        try
        {
            GacAttackExecutionApiClient.ExecutionResult result = await ExecutionClient.ExecuteAsync(
                AllyCode,
                attackId,
                ExecutionStatus,
                ExecutionBanners,
                string.IsNullOrWhiteSpace(ExecutionNotes) ? null : ExecutionNotes.Trim(),
                survivors,
                ExecutionStatus == "Failed" && ExecutionPreloadedTurnMeter);
            if (result.Envelope is null)
            {
                ExecutionError = result.Message ?? "No se ha podido registrar el resultado.";
                return;
            }

            LastExecution = result.Envelope;
            Planner = result.Envelope.Planner;
            CloseEditor();
            await Executed.InvokeAsync(result.Envelope);
        }
        catch (HttpRequestException)
        {
            ExecutionError = "No se ha podido registrar el resultado ni recalcular el siguiente ataque.";
        }
        finally
        {
            ExecutionBusy = false;
        }
    }

    private string DefenseZone(Guid defenseId) => Planner.Plan.VisibleDefenses
        .FirstOrDefault(defense => defense.Id == defenseId)?.Zone ?? string.Empty;

    private PlayerApiClient.RosterUnitViewModel? FindOwnRosterUnit(string definitionId) => OwnRoster.FirstOrDefault(unit =>
        string.Equals(unit.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase));

    private PlayerApiClient.RosterUnitViewModel? FindOpponentRosterUnit(string definitionId) => OpponentRoster.FirstOrDefault(unit =>
        string.Equals(unit.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase));

    private int WarRoomOrder(GacPlannerApiClient.AttackViewModel attack)
    {
        if (Optimization is null)
        {
            return int.MaxValue;
        }

        GacPlannerApiClient.OptimizationRecommendationViewModel[] ordered =
        [
            .. Optimization.Recommendations
                .OrderByDescending(recommendation => recommendation.Score)
                .ThenBy(recommendation => recommendation.StrategicCost)
        ];
        for (int index = 0; index < ordered.Length; index++)
        {
            GacPlannerApiClient.OptimizationRecommendationViewModel recommendation = ordered[index];
            if (recommendation.DefenseId == attack.DefenseId &&
                recommendation.TeamPresetId == attack.Team.Id)
            {
                return index + 1;
            }
        }

        return int.MaxValue;
    }

    private static string FriendlyConfidence(string confidence) => confidence switch
    {
        "High" => "Alta",
        "Medium" => "Media",
        "Low" => "Baja",
        _ => confidence
    };

    private static string FriendlyRisk(string risk) => risk switch
    {
        "Low" => "Bajo",
        "Medium" => "Medio",
        "High" => "Alto",
        _ => risk
    };

    private static string ExecutionLabel(GacAttackExecutionApiClient.ExecutedAttackViewModel execution)
    {
        string outcome = execution.Status == "Won" ? "Victoria" : "Fallo";
        string label = execution.Banners is int banners ? $"{outcome} · {banners} banners" : outcome;
        return execution.Status == "Failed"
            ? $"{label} · {execution.EnemySurvivors.Count} vivos"
            : label;
    }
}
