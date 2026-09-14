using Microsoft.AspNetCore.Components;

using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

[StreamRendering]
public partial class GrandArena
{
    private readonly List<PlayerApiClient.RosterUnitViewModel> playerRoster = [];
    private readonly List<PlayerApiClient.RosterUnitViewModel> opponentRoster = [];

    [Inject]
    private PlayerApiClient PlayerClient { get; set; } = null!;

    [Inject]
    private GacApiClient GacClient { get; set; } = null!;

    [Parameter]
    public long AllyCode { get; set; }

    private PlayerApiClient.PlayerViewModel? _player;
    private GacApiClient.CurrentGacResult? _result;
    private GacApiClient.BattleUnitViewModel? _selectedUnitSummary;
    private PlayerApiClient.RosterUnitViewModel? _selectedRosterUnit;
    private IReadOnlyCollection<string> _detailNotes = [];
    private string _detailEyebrow = "Ficha GAC";
    private string? _detailContextTitle;
    private string? _detailContext;
    private long? _detailRosterAllyCode;
    private bool _detailLoading;
    private bool _loading = true;
    private bool _scoutingLoading;
    private string? _error;

    private string HeaderDescription => _player is null
        ? "Analiza rival, riesgos, reservas y counters antes de fijar la ronda."
        : _scoutingLoading
            ? $"{_player.Name} · rival localizado · preparando roster, histórico y counters…"
            : $"{_player.Name} · #{FormatAllyCode(_player.AllyCode)} · decide qué conservar y cómo abrir el tablero.";

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        _scoutingLoading = false;
        _error = null;
        _result = null;
        CloseUnitDetails();
        playerRoster.Clear();
        opponentRoster.Clear();

        try
        {
            _player = await PlayerClient.GetAsync(AllyCode);
            if (_player is null)
            {
                _error = "No se ha encontrado el jugador. Cárgalo primero desde el inicio.";
                return;
            }

            GacApiClient.CurrentGacOpponentResult lookup = await GacClient.GetCurrentOpponentLookupAsync(AllyCode);
            if (lookup.Opponent is null)
            {
                _result = new GacApiClient.CurrentGacResult(null, lookup.Message);
                return;
            }

            _result = new GacApiClient.CurrentGacResult(
                new GacApiClient.CurrentGacScoutingViewModel(
                    lookup.Opponent,
                    Scouting: null,
                    RosterScouting: null,
                    BattlePlan: null,
                    Warnings: []),
                "Rival localizado. Preparando el scouting completo…");
            _loading = false;
            _scoutingLoading = true;
            StateHasChanged();
            await Task.Yield();

            GacApiClient.CurrentGacResult scoutingResult = await GacClient.GetCurrentScoutingAsync(AllyCode);
            if (scoutingResult.Scouting is not null)
            {
                _result = scoutingResult;
                await LoadVisualRostersAsync(scoutingResult.Scouting.Opponent.OpponentAllyCode);
            }
        }
        catch (HttpRequestException)
        {
            _error = "La API no está disponible en este momento.";
        }
        finally
        {
            _scoutingLoading = false;
            _loading = false;
        }
    }

    private async Task LoadVisualRostersAsync(long opponentAllyCode)
    {
        try
        {
            Task<IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel>> ownTask = LoadEntireRosterAsync(AllyCode);
            Task<IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel>> opponentTask = LoadEntireRosterAsync(opponentAllyCode);
            await Task.WhenAll(ownTask, opponentTask);

            playerRoster.AddRange(await ownTask);
            opponentRoster.AddRange(await opponentTask);
        }
        catch (HttpRequestException)
        {
            playerRoster.Clear();
            opponentRoster.Clear();
        }
    }

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

    private PlayerApiClient.RosterUnitViewModel? FindOwnUnit(string definitionId) =>
        FindUnit(playerRoster, definitionId);

    private PlayerApiClient.RosterUnitViewModel? FindOpponentUnit(string definitionId) =>
        FindUnit(opponentRoster, definitionId);

    private PlayerApiClient.RosterUnitViewModel? FindOpponentUnitByName(string name) =>
        opponentRoster.FirstOrDefault(unit => string.Equals(unit.Name, name, StringComparison.OrdinalIgnoreCase));

    private static PlayerApiClient.RosterUnitViewModel? FindUnit(
        IEnumerable<PlayerApiClient.RosterUnitViewModel> roster,
        string definitionId) => roster.FirstOrDefault(unit => string.Equals(
            unit.DefinitionId,
            definitionId,
            StringComparison.OrdinalIgnoreCase));

    private Task OpenThreatDetailAsync(GacApiClient.BattleThreatViewModel threat, long opponentAllyCode) =>
        OpenUnitDetailAsync(
            threat.Unit,
            opponentAllyCode,
            "Amenaza rival",
            $"{FriendlyCategory(threat.Category)} · prioridad {FriendlyPriority(threat.Priority)}",
            threat.Reason,
            [$"Score de amenaza: {threat.Score}.", BuildAbilityNote(threat.Unit)]);

    private Task OpenReserveDetailAsync(GacApiClient.AttackReserveViewModel reserve) =>
        OpenUnitDetailAsync(
            reserve.Unit,
            AllyCode,
            "Reserva de ataque",
            FriendlyRole(reserve.Role),
            reserve.Reason,
            [$"Prioridad {FriendlyPriority(reserve.Priority)}.", BuildAbilityNote(reserve.Unit)]);

    private Task OpenCounterTargetDetailAsync(GacApiClient.CounterSuggestionViewModel counter, long opponentAllyCode) =>
        OpenUnitDetailAsync(
            counter.Threat,
            opponentAllyCode,
            "Objetivo del counter",
            $"Counter recomendado · {FriendlyConfidence(counter.Confidence)}",
            counter.Rationale,
            BuildCounterNotes(counter));

    private Task OpenCounterUnitDetailAsync(
        GacApiClient.BattleUnitViewModel unit,
        GacApiClient.CounterSuggestionViewModel counter) =>
        OpenUnitDetailAsync(
            unit,
            AllyCode,
            "Pieza del counter",
            $"Contra {counter.Threat.Name}",
            counter.Rationale,
            BuildCounterNotes(counter));

    private async Task OpenUnitDetailAsync(
        GacApiClient.BattleUnitViewModel unit,
        long rosterAllyCode,
        string eyebrow,
        string contextTitle,
        string context,
        IReadOnlyCollection<string> notes)
    {
        _selectedUnitSummary = unit;
        _selectedRosterUnit = rosterAllyCode == AllyCode
            ? FindOwnUnit(unit.DefinitionId)
            : FindOpponentUnit(unit.DefinitionId);
        _detailEyebrow = eyebrow;
        _detailContextTitle = contextTitle;
        _detailContext = context;
        _detailNotes = notes;
        _detailRosterAllyCode = rosterAllyCode;

        if (_selectedRosterUnit is not null)
        {
            return;
        }

        _detailLoading = true;
        StateHasChanged();

        try
        {
            PlayerApiClient.RosterPageViewModel? roster = await PlayerClient.GetRosterAsync(
                rosterAllyCode,
                page: 1,
                pageSize: 10,
                search: unit.DefinitionId);
            _selectedRosterUnit = roster?.Items.FirstOrDefault(candidate =>
                string.Equals(candidate.DefinitionId, unit.DefinitionId, StringComparison.OrdinalIgnoreCase));
        }
        catch (HttpRequestException)
        {
            _detailNotes = [.. notes, "No se ha podido cargar la ficha enriquecida del roster; se muestra el resumen disponible en el plan."];
        }
        finally
        {
            _detailLoading = false;
        }
    }

    private void CloseUnitDetails()
    {
        _selectedUnitSummary = null;
        _selectedRosterUnit = null;
        _detailNotes = [];
        _detailContextTitle = null;
        _detailContext = null;
        _detailRosterAllyCode = null;
        _detailLoading = false;
    }

    private static IReadOnlyCollection<string> BuildCounterNotes(GacApiClient.CounterSuggestionViewModel counter)
    {
        var notes = new List<string>
        {
            $"Evidencia: {FriendlySource(counter.Source)} · confianza {FriendlyConfidence(counter.Confidence).ToLowerInvariant()}."
        };

        var metrics = new List<string>();
        if (counter.WinRate is not null)
        {
            metrics.Add($"{Percent(counter.WinRate.Value)} victorias");
        }
        if (counter.OneShotRate is not null)
        {
            metrics.Add($"{Percent(counter.OneShotRate.Value)} one-shot");
        }
        if (counter.Uses is not null)
        {
            metrics.Add($"{counter.Uses} muestras");
        }
        if (counter.AverageBanners is not null)
        {
            metrics.Add($"{counter.AverageBanners.Value:0.#} banners medios");
        }
        if (metrics.Count > 0)
        {
            notes.Add(string.Join(" · ", metrics) + ".");
        }
        if (counter.RequiresDatacronVerification)
        {
            notes.Add("Requiere verificar datacron y mods antes de ejecutar el ataque.");
        }

        return notes;
    }

    private static string BuildAbilityNote(GacApiClient.BattleUnitViewModel unit) =>
        $"Habilidades: {unit.ZetaCount} zetas · {unit.OmicronCount} omicrons.";

    private static string Percent(decimal value)
    {
        decimal normalized = value <= 1 ? value * 100 : value;
        return $"{Math.Clamp(normalized, 0, 100):0.#}%";
    }

    private static string FormatDelay(decimal? minutes)
    {
        if (minutes is null)
        {
            return "—";
        }

        decimal value = Math.Max(0, minutes.Value);
        return value < 60 ? $"{value:0} min" : $"{value / 60:0.#} h";
    }

    private static string CompactNumber(long value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000d:0.##}M",
        >= 1_000 => $"{value / 1_000d:0.#}K",
        _ => value.ToString("N0")
    };

    private static string SignedCompact(long value) => value >= 0
        ? $"+{CompactNumber(value)}"
        : $"-{CompactNumber(Math.Abs(value))}";

    private static string UnitProgress(GacApiClient.BattleUnitViewModel unit) =>
        unit.IsShip ? "Nave" : $"R{unit.RelicTier}";

    private static string FriendlySource(string source) => source switch
    {
        "GlobalHistoricalCounterData" => "Histórico global",
        "HistoricalCounterPattern" => "Histórico del rival",
        "ObservedCounterStatistics" => "Histórico observado",
        "StoredGacHistory" => "Histórico GAC",
        "RosterStrengthHeuristic" => "Estimación por roster",
        _ => source
    };

    private static string FriendlyDefenseSource(string source) => source switch
    {
        "HistoricalPattern" => "Basado en defensas históricas",
        "RosterThreatHeuristic" => "Estimación por fuerza del roster",
        _ => source
    };

    private static string FriendlyConfidence(string confidence) => confidence switch
    {
        "High" => "Alta",
        "Medium" => "Media",
        "Low" => "Baja",
        _ => confidence
    };

    private static string ConfidenceTone(string confidence) => confidence switch
    {
        "High" => "success",
        "Medium" => "warning",
        _ => "neutral"
    };

    private static string PriorityTone(string priority) => priority switch
    {
        "Critical" => "danger",
        "High" => "warning",
        "Medium" => "accent",
        _ => "neutral"
    };

    private static string FriendlyPriority(string priority) => priority switch
    {
        "Critical" => "Crítica",
        "High" => "Alta",
        "Medium" => "Media",
        "Watch" => "Vigilar",
        "Low" => "Baja",
        _ => priority
    };

    private static string FriendlyCategory(string category) => category switch
    {
        "GalacticLegend" => "Leyenda galáctica",
        "Omicron" => "Omicron",
        "Fleet" => "Flota",
        "HighPower" => "Alto GP",
        _ => category
    };

    private static string FriendlyRole(string role) => role switch
    {
        "GlAnswer" => "Respuesta GL",
        "GacSpecialist" => "Especialista GAC",
        "FleetAnchor" => "Ancla de flota",
        "HighPowerFlex" => "Comodín de alto GP",
        _ => role
    };

    private static string FormatAllyCode(long allyCode)
    {
        string value = allyCode.ToString("000000000");
        return $"{value[..3]}-{value[3..6]}-{value[6..]}";
    }
}
