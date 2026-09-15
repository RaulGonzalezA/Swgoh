using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class Home
{
    private string _allyCode = string.Empty;
    private bool _loading;
    private bool _gacLoading;
    private string? _gacMessage;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationToken LifetimeToken => _disposed ? new CancellationToken(canceled: true) : _lifetime.Token;
    private bool _disposed;
    private bool _searched;
    private bool _showPlayerPicker;
    private string? _error;
    private PlayerApiClient.PlayerViewModel? _player;
    private PlayerApiClient.PlayerRosterAnalysisViewModel? _analysis;
    private GacPlannerApiClient.PlannerViewModel? _planner;
    private GacPlannerApiClient.OptimizationViewModel? _gacOptimization;
    private ConquestApiClient.PlanViewModel? _conquest;
    private ConquestApiClient.OptimizationViewModel? _conquestOptimization;
    private ConquestDailyPlanApiClient.DailyPlanViewModel? _dailyPlan;

    private GacPlannerApiClient.AttackViewModel? PlannedGacAttack => _planner?.Plan.Attacks
        .Where(attack => string.Equals(attack.Status, "Planned", StringComparison.OrdinalIgnoreCase))
        .Where(attack => _planner.Plan.VisibleDefenses.Any(defense => defense.Id == attack.DefenseId && !defense.Defeated))
        .OrderBy(attack => attack.Attempt)
        .FirstOrDefault();

    private GacPlannerApiClient.VisibleDefenseViewModel? PlannedGacDefense => PlannedGacAttack is null
        ? null
        : _planner?.Plan.VisibleDefenses.FirstOrDefault(defense => defense.Id == PlannedGacAttack.DefenseId);

    private GacPlannerApiClient.OptimizationRecommendationViewModel? TopGacRecommendation =>
        _gacOptimization?.Recommendations.FirstOrDefault();

    private GacPlannerApiClient.TeamPresetViewModel? GacRecommendedPreset => TopGacRecommendation is null
        ? null
        : _planner?.Presets.FirstOrDefault(preset => preset.Id == TopGacRecommendation.TeamPresetId);

    private GacPlannerApiClient.VisibleDefenseViewModel? GacRecommendedDefense => TopGacRecommendation is null
        ? null
        : _planner?.Plan.VisibleDefenses.FirstOrDefault(defense => defense.Id == TopGacRecommendation.DefenseId);

    private ConquestApiClient.TeamViewModel? TopConquestRecommendation =>
        _conquestOptimization?.Recommendations.FirstOrDefault();

    private int GacPendingDefenses => _planner?.Plan.VisibleDefenses.Count(defense => !defense.Defeated) ?? 0;

    private bool HasHighConfidenceGacPlan => TopGacRecommendation is { Score: >= 85m, Confidence: "High" };

    private int RewardPointsMissing
    {
        get
        {
            if (_dailyPlan?.TargetRewardPoints is int dailyTarget)
            {
                return Math.Max(0, dailyTarget - _dailyPlan.StartingRewardPoints);
            }

            return _conquest?.TargetRewardPoints is int target
                ? Math.Max(0, target - _conquest.CurrentRewardPoints)
                : 0;
        }
    }

    private int? BattlesToRewardTarget
    {
        get
        {
            if (_dailyPlan?.TargetRewardPoints is not int target)
            {
                return null;
            }

            ConquestDailyPlanApiClient.DailyPlanStepViewModel? step = _dailyPlan.Steps
                .FirstOrDefault(item => item.ProjectedRewardPointsAfterBattle >= target);
            return step?.BattleNumber;
        }
    }

    private bool RewardTargetSoon => RewardPointsMissing > 0 && BattlesToRewardTarget is > 0 and <= 3;

    private int ImmediateConquestBattles => Math.Min(3, _dailyPlan?.PlannedBattles ?? 0);

    private HomeActionKind PrimaryAction
    {
        get
        {
            if (PlannedGacAttack is not null)
            {
                return HomeActionKind.GacExecute;
            }

            if (RewardTargetSoon)
            {
                return HomeActionKind.ConquestRewardPush;
            }

            if (HasHighConfidenceGacPlan)
            {
                return HomeActionKind.GacPlan;
            }

            if (_dailyPlan?.PlannedBattles > 0)
            {
                return HomeActionKind.ConquestBattles;
            }

            if (TopGacRecommendation is not null)
            {
                return HomeActionKind.GacPlan;
            }

            if (_dailyPlan?.StopReason == "NoUsableCharacters" ||
                (_dailyPlan?.PlannedBattles == 0 && _dailyPlan.RecoveryPriority.Count > 0))
            {
                return HomeActionKind.ConquestWaitStamina;
            }

            if (_dailyPlan?.StopReason == "EnergyBudgetExhausted")
            {
                return HomeActionKind.ConquestWaitEnergy;
            }

            if (TopConquestRecommendation is not null)
            {
                return HomeActionKind.ConquestOptimize;
            }

            return HomeActionKind.Configure;
        }
    }

    private string HeaderDescription
    {
        get
        {
            string guild = string.IsNullOrWhiteSpace(_player?.GuildName) ? string.Empty : $"{_player.GuildName} · ";
            return $"{guild}#{FormatAllyCode(_player!.AllyCode)} · una prioridad calculada con el estado actual de GAC y Conquista.";
        }
    }

    private string PrimaryActionTitle => PrimaryAction switch
    {
        HomeActionKind.GacExecute => $"Ataca a {PlannedGacDefense?.Squad.Leader.Name ?? "esa defensa"} con {PlannedGacAttack?.Team.Name}",
        HomeActionKind.ConquestRewardPush => $"Haz {BattlesToRewardTarget} combate(s): te faltan {RewardPointsMissing} puntos para {RewardTargetLabel}",
        HomeActionKind.ConquestBattles => $"Haz los próximos {ImmediateConquestBattles} combates de Conquista",
        HomeActionKind.GacPlan => $"Prepara {TopGacRecommendation?.TeamName} contra {TopGacRecommendation?.DefenseName}",
        HomeActionKind.ConquestWaitStamina => "Espera stamina antes de volver a gastar ese equipo",
        HomeActionKind.ConquestWaitEnergy => "Espera energía: ahora no compensa forzar otro combate",
        HomeActionKind.ConquestOptimize => "Usa este combate para avanzar varias hazañas a la vez",
        _ => "Configura el siguiente objetivo que quieras optimizar"
    };

    private string PrimaryActionDescription => PrimaryAction switch
    {
        HomeActionKind.GacExecute => "Ese ataque ya está planificado y el equipo está reservado. Ejecútalo y registra victoria/fallo y banners para que el Planner aprenda y recalcule el siguiente.",
        HomeActionKind.ConquestRewardPush => $"El plan diario alcanza {RewardTargetLabel} dentro de tres combates. Es una ganancia inmediata y medible antes de volver a repartir recursos.",
        HomeActionKind.ConquestBattles => $"Tienes una secuencia viable con stamina y energía disponibles. Hazla en orden para maximizar progreso por combate y evitar gastar equipos de reserva.",
        HomeActionKind.GacPlan => $"Es la mejor combinación disponible para una defensa aún pendiente. Score {TopGacRecommendation?.Score:0.#} con confianza {ConfidenceLabel(TopGacRecommendation?.Confidence ?? string.Empty)}.",
        HomeActionKind.ConquestWaitStamina => "El plan no encuentra otro equipo utilizable sin castigar demasiado la reserva. Mientras recuperan stamina, GAC y roster siguen disponibles.",
        HomeActionKind.ConquestWaitEnergy => "El plan diario se ha detenido por presupuesto de energía. No hay una acción de Conquista mejor hasta recuperar energía o cambiar el objetivo.",
        HomeActionKind.ConquestOptimize => TopConquestRecommendation?.Rationale ?? "Hay una combinación que avanza varias hazañas en el mismo combate.",
        _ => "No hay una acción urgente calculable con los datos actuales. Añade defensas visibles en GAC o configura las hazañas/objetivo de Conquista."
    };

    private string PrimaryActionTone => PrimaryAction switch
    {
        HomeActionKind.GacExecute or HomeActionKind.GacPlan => "accent",
        HomeActionKind.ConquestRewardPush or HomeActionKind.ConquestBattles or HomeActionKind.ConquestOptimize => "success",
        HomeActionKind.ConquestWaitStamina or HomeActionKind.ConquestWaitEnergy => "warning",
        _ => "neutral"
    };

    private string PrimaryActionBadge => PrimaryAction switch
    {
        HomeActionKind.GacExecute => "GAC · ejecutar",
        HomeActionKind.GacPlan => "GAC · preparar",
        HomeActionKind.ConquestRewardPush => "Conquista · recompensa",
        HomeActionKind.ConquestBattles => "Conquista · ahora",
        HomeActionKind.ConquestWaitStamina => "Conquista · recuperar",
        HomeActionKind.ConquestWaitEnergy => "Conquista · esperar",
        HomeActionKind.ConquestOptimize => "Conquista · optimizar",
        _ => "Sin urgencias"
    };

    private string PrimaryActionHref => PrimaryAction switch
    {
        HomeActionKind.GacExecute => $"/gac/{_player!.AllyCode}/planner#gac-execution",
        HomeActionKind.GacPlan => $"/gac/{_player!.AllyCode}/planner",
        HomeActionKind.ConquestRewardPush or HomeActionKind.ConquestBattles or HomeActionKind.ConquestWaitStamina or
            HomeActionKind.ConquestWaitEnergy or HomeActionKind.ConquestOptimize => $"/conquest/{_player!.AllyCode}",
        _ => $"/data"
    };

    private string PrimaryActionText => PrimaryAction switch
    {
        HomeActionKind.GacExecute => "Abrir modo ejecución",
        HomeActionKind.GacPlan => "Abrir Planner y confirmar",
        HomeActionKind.ConquestRewardPush or HomeActionKind.ConquestBattles or HomeActionKind.ConquestOptimize => "Abrir plan de Conquista",
        HomeActionKind.ConquestWaitStamina => "Ver recuperación y plan",
        HomeActionKind.ConquestWaitEnergy => "Ver energía y objetivo",
        _ => "Revisar datos disponibles"
    };

    private string RewardTargetLabel => string.IsNullOrWhiteSpace(_dailyPlan?.RewardTargetName)
        ? string.IsNullOrWhiteSpace(_conquest?.RewardTargetName) ? "la recompensa objetivo" : _conquest.RewardTargetName!
        : _dailyPlan.RewardTargetName!;

    private string EnergySummary
    {
        get
        {
            if (_dailyPlan?.AvailableEnergy is not int available)
            {
                return "sin límite configurado";
            }

            int remaining = _dailyPlan.EnergyRemaining ?? Math.Max(0, available - _dailyPlan.EnergySpent);
            return $"{available} → {remaining}";
        }
    }

    private string ConquestStaminaSummary
    {
        get
        {
            ConquestDailyPlanApiClient.DailyPlanStepViewModel? step = _dailyPlan?.Steps.FirstOrDefault();
            return step is null ? "sin combate" : $"{step.AverageStaminaBefore:0} → {step.AverageStaminaAfter:0}";
        }
    }

    private IReadOnlyCollection<HomeQueueItem> ActionQueue
    {
        get
        {
            var items = new List<HomeQueueItem>();

            if (PrimaryAction != HomeActionKind.GacExecute && PlannedGacAttack is not null)
            {
                items.Add(new HomeQueueItem(
                    "Ejecutar el ataque GAC ya reservado",
                    $"{PlannedGacAttack.Team.Name} · intento #{PlannedGacAttack.Attempt}",
                    $"/gac/{_player!.AllyCode}/planner#gac-execution"));
            }
            else if (PrimaryAction != HomeActionKind.GacPlan && TopGacRecommendation is not null)
            {
                items.Add(new HomeQueueItem(
                    "Preparar el siguiente counter de GAC",
                    $"{TopGacRecommendation.TeamName} → {TopGacRecommendation.DefenseName} · score {TopGacRecommendation.Score:0.#}",
                    $"/gac/{_player!.AllyCode}/planner"));
            }

            if (!IsConquestPrimary && _dailyPlan?.PlannedBattles > 0)
            {
                items.Add(new HomeQueueItem(
                    $"Completar {Math.Min(3, _dailyPlan.PlannedBattles)} combate(s) de Conquista",
                    RewardPointsMissing > 0
                        ? $"Faltan {RewardPointsMissing} puntos para {RewardTargetLabel}"
                        : $"{_dailyPlan.ProjectedRewardPointsGained} puntos proyectados",
                    $"/conquest/{_player!.AllyCode}"));
            }

            if (IsDataStale)
            {
                items.Add(new HomeQueueItem(
                    "Actualizar datos antes de una decisión importante",
                    $"El roster tiene {RelativeUpdated(_player!.UpdatedAtUtc)}",
                    "/data"));
            }

            return [.. items.Take(3)];
        }
    }

    private bool IsConquestPrimary => PrimaryAction is
        HomeActionKind.ConquestRewardPush or
        HomeActionKind.ConquestBattles or
        HomeActionKind.ConquestWaitStamina or
        HomeActionKind.ConquestWaitEnergy or
        HomeActionKind.ConquestOptimize;

    private string GacContextValue => _planner is null
        ? "Sin ronda"
        : PlannedGacAttack is not null ? "Ataque listo" : $"{GacPendingDefenses} pendientes";

    private string GacContextHint => _planner is null
        ? "No hay rival activo disponible"
        : $"vs {_planner.Opponent.OpponentName} · {_planner.Opponent.Format}";

    private string ConquestContextValue => _conquest is null
        ? "Sin plan"
        : RewardPointsMissing > 0 ? $"{RewardPointsMissing} pts para objetivo" : $"{_conquest.EarnedFeatPoints} pts";

    private string ConquestContextHint => _dailyPlan is null
        ? "Configura hazañas y recursos"
        : _dailyPlan.PlannedBattles > 0
            ? $"{_dailyPlan.PlannedBattles} combates viables · {_dailyPlan.StopReason}"
            : DailyStopReason(_dailyPlan.StopReason);

    private bool IsDataStale => DateTimeOffset.UtcNow - _player!.UpdatedAtUtc >= TimeSpan.FromHours(24);
    private string DataContextValue => IsDataStale ? "Revisar" : "Actualizados";
    private string DataContextHint => $"Roster {RelativeUpdated(_player!.UpdatedAtUtc)} · GAC {PlannerAge} · Conquista {ConquestAge}";
    private string DataMetricTone => IsDataStale ? "warning" : "success";
    private string PlannerAge => _planner is null ? "sin dato" : RelativeUpdated(_planner.Plan.UpdatedAtUtc);
    private string ConquestAge => _conquest is null ? "sin dato" : RelativeUpdated(_conquest.UpdatedAtUtc);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _player is not null || _searched)
        {
            return;
        }

        if (await PlayerContext.RestoreAsync() && Session.AllyCode is long allyCode)
        {
            _allyCode = allyCode.ToString();
            await LoadPlayerAsync(allyCode, persistPreference: false);
            await RenderProgressAsync();
        }
    }

    private async Task LoadAsync()
    {
        string normalized = _allyCode
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!long.TryParse(normalized, out long allyCode) || normalized.Length != 9)
        {
            _error = "Introduce un Ally Code válido de 9 dígitos.";
            return;
        }

        await LoadPlayerAsync(allyCode, persistPreference: true);
    }

    private async Task LoadPlayerAsync(long allyCode, bool persistPreference)
    {
        _loading = true;
        _searched = true;
        _error = null;
        _analysis = null;
        _planner = null;
        _gacOptimization = null;
        _conquest = null;
        _conquestOptimization = null;
        _dailyPlan = null;

        try
        {
            bool activated = persistPreference
                ? await PlayerContext.ConnectAsync(allyCode, LifetimeToken)
                : await PlayerContext.ActivateAsync(allyCode, persistPreference: false, LifetimeToken);

            if (!activated)
            {
                _player = null;
                _error = "No se ha encontrado el perfil. Comprueba el Ally Code y vuelve a intentarlo.";
                return;
            }

            _player = await PlayerClient.GetAsync(allyCode, LifetimeToken);
            if (_player is null)
            {
                _error = "El perfil activo no está disponible en la API.";
                return;
            }

            _showPlayerPicker = false;
            _gacLoading = true;
            _gacMessage = null;
            await RenderProgressAsync();
            _ = LoadAnalysisAsync(allyCode);
            _ = LoadGacAsync(allyCode);
            _ = LoadConquestAsync(allyCode);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _error = "Espera 30 segundos antes de volver a importar este perfil.";
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.BadGateway)
        {
            _error = "No se ha podido importar el perfil desde SWGOH. Inténtalo de nuevo en unos momentos.";
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (OperationCanceledException)
        {
            _error = "La conexión ha tardado demasiado. Inténtalo de nuevo en unos momentos.";
        }
        catch (HttpRequestException)
        {
            _error = "La API no está disponible en este momento. Revisa que AppHost y Swgoh.Api estén arrancados.";
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadGacAsync(long allyCode)
    {
        try
        {
            GacPlannerApiClient.PlannerResult plannerResult = await PlannerClient.GetCurrentAsync(allyCode, LifetimeToken);
            _planner = plannerResult.Planner;
            _gacMessage = plannerResult.Message;
            if (_planner is null)
            {
                return;
            }

            GacPlannerApiClient.OptimizationResult optimization = await PlannerClient.OptimizeCurrentAsync(
                allyCode,
                "FillGaps",
                apply: false,
                cancellationToken: LifetimeToken);
            _gacOptimization = optimization.Envelope?.Optimization;
        }
        catch (HttpRequestException)
        {
            _planner = null;
            _gacOptimization = null;
            _gacMessage = "No se ha podido consultar Gran Arena. El resto de tus datos sigue disponible.";
        }
        catch (OperationCanceledException) when (!_disposed)
        {
            _gacMessage = "La búsqueda de Gran Arena ha agotado su tiempo. Puedes volver a intentarlo más tarde.";
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            _gacMessage = "Gran Arena está tardando demasiado. El resto de tus datos sigue disponible.";
        }
        finally
        {
            _gacLoading = false;
            await RenderProgressAsync();
        }
    }

    private async Task LoadConquestAsync(long allyCode)
    {
        try
        {
            _conquest = await ConquestClient.GetCurrentAsync(allyCode, LifetimeToken);
            if (_conquest is null)
            {
                return;
            }

            Task<ConquestApiClient.OptimizationViewModel?> optimizationTask = ConquestClient.OptimizeCurrentAsync(allyCode, LifetimeToken);
            Task<ConquestDailyPlanApiClient.DailyPlanViewModel?> dailyPlanTask = DailyPlanClient.BuildAsync(allyCode, maxBattles: 6, LifetimeToken);
            await Task.WhenAll(optimizationTask, dailyPlanTask);
            _conquestOptimization = await optimizationTask;
            _dailyPlan = await dailyPlanTask;
        }
        catch (HttpRequestException)
        {
            _conquest = null;
            _conquestOptimization = null;
            _dailyPlan = null;
        }
        finally
        {
            await RenderProgressAsync();
        }
    }

    private async Task LoadAnalysisAsync(long allyCode)
    {
        try
        {
            _analysis = await PlayerClient.GetAnalysisAsync(allyCode, LifetimeToken);
        }
        catch (HttpRequestException)
        {
            _analysis = null;
        }
        finally
        {
            await RenderProgressAsync();
        }
    }

    private Task RenderProgressAsync() => _disposed ? Task.CompletedTask : InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void ShowPlayerPicker()
    {
        _showPlayerPicker = true;
        _allyCode = string.Empty;
        _error = null;
    }

    private void HidePlayerPicker()
    {
        _showPlayerPicker = false;
        _allyCode = _player?.AllyCode.ToString() ?? string.Empty;
        _error = null;
    }

    private static string FormatAllyCode(long allyCode)
    {
        string value = allyCode.ToString("000000000");
        return $"{value[..3]}-{value[3..6]}-{value[6..]}";
    }

    private static string RelativeUpdated(DateTimeOffset updatedAt)
    {
        TimeSpan age = DateTimeOffset.UtcNow - updatedAt;
        return age.TotalMinutes switch
        {
            < 2 => "ahora",
            < 60 => $"hace {Math.Max(1, (int)age.TotalMinutes)} min",
            < 1440 => $"hace {Math.Max(1, (int)age.TotalHours)} h",
            < 2880 => "ayer",
            _ => updatedAt.ToLocalTime().ToString("dd/MM/yyyy")
        };
    }

    private static string ConfidenceLabel(string confidence) => confidence switch
    {
        "High" => "Alta",
        "Medium" => "Media",
        "Low" => "Baja",
        _ => confidence
    };

    private static string Percent(decimal value)
    {
        decimal normalized = value <= 1m ? value * 100m : value;
        return $"{Math.Clamp(normalized, 0m, 100m):0.#}%";
    }

    private static string DailyStopReason(string value) => value switch
    {
        "RewardTargetReached" => "Objetivo de recompensa alcanzado",
        "EnergyBudgetExhausted" => "Sin energía suficiente",
        "AllFeatsCompleted" => "Hazañas proyectadas completadas",
        "NoUsableCharacters" => "Esperando stamina",
        "NoViableTeam" => "Sin otro equipo viable",
        "RosterUnavailable" => "Roster no disponible",
        _ => "Límite diario alcanzado"
    };

    private enum HomeActionKind
    {
        GacExecute,
        GacPlan,
        ConquestRewardPush,
        ConquestBattles,
        ConquestWaitStamina,
        ConquestWaitEnergy,
        ConquestOptimize,
        Configure
    }

    private sealed record HomeQueueItem(string Title, string Description, string Href);
}
