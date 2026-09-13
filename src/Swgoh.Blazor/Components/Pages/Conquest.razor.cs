using Microsoft.AspNetCore.Components;

using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class Conquest
{
    private const string AssetBaseUrl = "https://swgoh.gg/static/img/assets/";
    private readonly List<PlayerApiClient.RosterUnitViewModel> roster = [];
    private readonly List<FeatDraft> feats = [];

    [Inject]
    private PlayerApiClient PlayerClient { get; set; } = null!;

    [Inject]
    private ConquestApiClient ConquestClient { get; set; } = null!;

    [Parameter]
    public long AllyCode { get; set; }

    protected bool Loading { get; private set; } = true;
    protected bool Saving { get; private set; }
    protected bool Optimizing { get; private set; }
    protected bool ShowFeatBuilder { get; private set; }
    protected string? Error { get; private set; }
    protected ConquestApiClient.OptimizationViewModel? Optimization { get; private set; }

    protected string EventId { get; set; } = $"conquest-{DateTimeOffset.UtcNow:yyyy-MM}";
    protected string EventName { get; set; } = "Conquista actual";
    protected string Difficulty { get; set; } = "Hard";

    protected string NewFeatName { get; set; } = string.Empty;
    protected string NewFeatScope { get; set; } = "Sector";
    protected int NewFeatSector { get; set; } = 1;
    protected int NewFeatPoints { get; set; } = 5;
    protected int NewFeatTarget { get; set; } = 5;
    protected int NewFeatProgress { get; set; }
    protected int NewFeatProgressPerBattle { get; set; } = 1;
    protected string NewFeatRuleType { get; set; } = "Faction";
    protected string NewFeatFaction { get; set; } = string.Empty;
    protected string NewFeatSpecificUnitId { get; set; } = string.Empty;
    protected int NewFeatMinimumMatchingUnits { get; set; } = 1;

    protected List<FeatDraft> Feats => feats;
    protected int CharacterCount => roster.Count(unit => !unit.IsShip);
    protected int CompletedFeatCount => feats.Count(feat => feat.IsComplete);
    protected int EarnedFeatPoints => feats.Where(feat => feat.IsComplete).Sum(feat => feat.Points);
    protected int AvailableFeatPoints => feats.Where(feat => !feat.IsComplete).Sum(feat => feat.Points);

    protected IReadOnlyCollection<string> FactionOptions =>
        [
            .. roster
                .Where(unit => !unit.IsShip)
                .SelectMany(unit => unit.Factions)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        ];

    protected IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> CharacterOptions =>
        [
            .. roster
                .Where(unit => !unit.IsShip)
                .OrderByDescending(unit => unit.GalacticPower)
                .ThenBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)
        ];

    protected bool CanAddFeat =>
        !string.IsNullOrWhiteSpace(NewFeatName) &&
        NewFeatPoints > 0 &&
        NewFeatTarget > 0 &&
        NewFeatProgress is >= 0 &&
        NewFeatProgress <= NewFeatTarget &&
        NewFeatProgressPerBattle > 0 &&
        NewFeatMinimumMatchingUnits is >= 1 and <= 5 &&
        (NewFeatRuleType switch
        {
            "Faction" => !string.IsNullOrWhiteSpace(NewFeatFaction),
            "SpecificUnits" => !string.IsNullOrWhiteSpace(NewFeatSpecificUnitId),
            _ => true
        });

    protected override async Task OnParametersSetAsync()
    {
        Loading = true;
        Error = null;
        Optimization = null;
        roster.Clear();
        feats.Clear();

        try
        {
            Task<ConquestApiClient.PlanViewModel?> planTask = ConquestClient.GetCurrentAsync(AllyCode);
            Task<IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel>> rosterTask = LoadEntireRosterAsync();
            await Task.WhenAll(planTask, rosterTask);

            roster.AddRange(await rosterTask);
            ConquestApiClient.PlanViewModel? plan = await planTask;
            if (plan is not null)
            {
                MapPlan(plan);
            }
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

    protected void ToggleFeatBuilder() => ShowFeatBuilder = !ShowFeatBuilder;

    protected void AddFeat()
    {
        if (!CanAddFeat)
        {
            return;
        }

        IReadOnlyCollection<string> units = NewFeatRuleType == "SpecificUnits"
            ? [NewFeatSpecificUnitId]
            : [];
        feats.Add(new FeatDraft
        {
            Id = Guid.NewGuid(),
            Name = NewFeatName.Trim(),
            Scope = NewFeatScope,
            Sector = NewFeatScope == "Global" ? null : NewFeatSector,
            Points = NewFeatPoints,
            Target = NewFeatTarget,
            Progress = NewFeatProgress,
            ExpectedProgressPerBattle = NewFeatProgressPerBattle,
            RuleType = NewFeatRuleType,
            Faction = NewFeatRuleType == "Faction" ? NewFeatFaction : null,
            UnitDefinitionIds = units,
            MinimumMatchingUnits = NewFeatMinimumMatchingUnits
        });
        ResetFeatBuilder();
        Optimization = null;
    }

    protected void RemoveFeat(Guid id)
    {
        feats.RemoveAll(feat => feat.Id == id);
        Optimization = null;
    }

    protected async Task SavePlanAsync()
    {
        Saving = true;
        Error = null;
        try
        {
            await PersistPlanAsync();
        }
        catch (HttpRequestException)
        {
            Error = "No se ha podido guardar la Conquista. Revisa los objetivos, progreso y requisitos de las hazañas.";
        }
        finally
        {
            Saving = false;
        }
    }

    protected async Task OptimizeAsync()
    {
        Optimizing = true;
        Error = null;
        try
        {
            await PersistPlanAsync();
            Optimization = await ConquestClient.OptimizeCurrentAsync(AllyCode);
            if (Optimization is null)
            {
                Error = "Guarda primero una Conquista con al menos una hazaña pendiente.";
            }
        }
        catch (HttpRequestException)
        {
            Error = "No se ha podido calcular el plan de Conquista.";
        }
        finally
        {
            Optimizing = false;
        }
    }

    protected static string ScopeLabel(FeatDraft feat) => feat.Scope switch
    {
        "Global" => "Global",
        "Boss" => $"Jefe · Sector {feat.Sector}",
        _ => $"Sector {feat.Sector}"
    };

    protected static string RuleLabel(FeatDraft feat) => feat.RuleType switch
    {
        "Faction" => $"{feat.MinimumMatchingUnits} unidad(es) de {feat.Faction}",
        "SpecificUnits" => feat.UnitDefinitionIds.Count == 1
            ? $"Requiere {feat.UnitDefinitionIds.First()}"
            : $"Requiere {feat.MinimumMatchingUnits} de {feat.UnitDefinitionIds.Count} unidades concretas",
        _ => "Cualquier equipo de personajes"
    };

    protected static string ProgressLabel(PlayerApiClient.RosterUnitViewModel unit) =>
        unit.RelicTier > 0 ? $"R{unit.RelicTier}" : $"G{unit.GearTier}";

    protected static string? GetPortraitUrl(string? thumbnailName)
    {
        if (string.IsNullOrWhiteSpace(thumbnailName))
        {
            return null;
        }

        string asset = thumbnailName.Trim();
        if (!asset.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            asset += ".png";
        }

        return AssetBaseUrl + Uri.EscapeDataString(asset);
    }

    protected static string Initial(string name) => string.IsNullOrWhiteSpace(name)
        ? "?"
        : char.ToUpperInvariant(name.Trim()[0]).ToString();

    private async Task PersistPlanAsync()
    {
        var request = new ConquestApiClient.SavePlanRequest(
            EventId.Trim(),
            EventName.Trim(),
            Difficulty,
            [
                .. feats.Select(feat => new ConquestApiClient.SaveFeatRequest(
                    feat.Id,
                    feat.Name,
                    feat.Scope,
                    feat.Sector,
                    feat.Points,
                    feat.Target,
                    Math.Clamp(feat.Progress, 0, Math.Max(1, feat.Target)),
                    feat.ExpectedProgressPerBattle,
                    feat.RuleType,
                    feat.Faction,
                    feat.UnitDefinitionIds,
                    feat.MinimumMatchingUnits))
            ]);
        ConquestApiClient.PlanViewModel plan = await ConquestClient.SaveCurrentAsync(AllyCode, request);
        MapPlan(plan);
        Optimization = null;
    }

    private void MapPlan(ConquestApiClient.PlanViewModel plan)
    {
        EventId = plan.EventId;
        EventName = plan.Name;
        Difficulty = plan.Difficulty;
        feats.Clear();
        feats.AddRange(plan.Feats.Select(feat => new FeatDraft
        {
            Id = feat.Id,
            Name = feat.Name,
            Scope = feat.Scope,
            Sector = feat.Sector,
            Points = feat.Points,
            Target = feat.Target,
            Progress = feat.Progress,
            ExpectedProgressPerBattle = feat.ExpectedProgressPerBattle,
            RuleType = feat.RuleType,
            Faction = feat.Faction,
            UnitDefinitionIds = feat.UnitDefinitionIds,
            MinimumMatchingUnits = feat.MinimumMatchingUnits
        }));
    }

    private async Task<IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel>> LoadEntireRosterAsync()
    {
        var units = new List<PlayerApiClient.RosterUnitViewModel>();
        int page = 1;
        int totalPages;
        do
        {
            PlayerApiClient.RosterPageViewModel? result = await PlayerClient.GetRosterAsync(
                AllyCode,
                page,
                pageSize: 100,
                type: "Character",
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

    private void ResetFeatBuilder()
    {
        NewFeatName = string.Empty;
        NewFeatScope = "Sector";
        NewFeatSector = 1;
        NewFeatPoints = 5;
        NewFeatTarget = 5;
        NewFeatProgress = 0;
        NewFeatProgressPerBattle = 1;
        NewFeatRuleType = "Faction";
        NewFeatFaction = string.Empty;
        NewFeatSpecificUnitId = string.Empty;
        NewFeatMinimumMatchingUnits = 1;
        ShowFeatBuilder = false;
    }

    protected sealed class FeatDraft
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Scope { get; init; } = "Sector";
        public int? Sector { get; init; }
        public int Points { get; init; }
        public int Target { get; init; }
        public int Progress { get; set; }
        public int ExpectedProgressPerBattle { get; init; }
        public string RuleType { get; init; } = "Faction";
        public string? Faction { get; init; }
        public IReadOnlyCollection<string> UnitDefinitionIds { get; init; } = [];
        public int MinimumMatchingUnits { get; init; }
        public bool IsComplete => Target > 0 && Progress >= Target;
    }
}
