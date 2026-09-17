using Swgoh.Application.Abstractions;
using Swgoh.Application.Conquest;
using Swgoh.Application.Eras;
using Swgoh.Application.Gac;
using Swgoh.Application.Players;
using Swgoh.Application.TerritoryBattles;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Investments;

public interface IInvestmentOptimizerService
{
    Task<InvestmentOptimizationResult?> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default);
}

internal sealed class InvestmentOptimizerService(
    IPlayerProfileService playerProfileService,
    IGacPlannerService gacPlannerService,
    IRiseOfEmpireService riseOfEmpireService,
    IConquestService conquestService,
    IEraService eraService,
    IPlayerInventoryService inventoryService,
    IClock clock) : IInvestmentOptimizerService
{
    private const int MaximumRecommendations = 20;

    public async Task<InvestmentOptimizationResult?> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        PlayerProfile? player = await playerProfileService.GetAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        Task<ModuleFetch<GacPlannerLookup>> gacTask = CaptureAsync(
            () => gacPlannerService.GetCurrentAsync(allyCode, cancellationToken),
            cancellationToken);
        Task<ModuleFetch<RiseOfEmpireAnalysis?>> roteTask = CaptureAsync(
            () => riseOfEmpireService.GetAsync(allyCode, cancellationToken),
            cancellationToken);
        Task<ModuleFetch<ConquestOptimizationResult?>> conquestTask = CaptureAsync(
            () => conquestService.OptimizeCurrentAsync(allyCode, cancellationToken),
            cancellationToken);
        Task<ModuleFetch<EraAnalysis?>> eraTask = CaptureAsync(
            () => eraService.GetCurrentAsync(allyCode, cancellationToken),
            cancellationToken);
        Task<ModuleFetch<PlayerInventorySnapshot?>> inventoryTask = CaptureAsync(
            () => inventoryService.GetAsync(allyCode, cancellationToken),
            cancellationToken);

        await Task.WhenAll(gacTask, roteTask, conquestTask, eraTask, inventoryTask).ConfigureAwait(false);

        ModuleFetch<GacPlannerLookup> gac = await gacTask.ConfigureAwait(false);
        ModuleFetch<RiseOfEmpireAnalysis?> rote = await roteTask.ConfigureAwait(false);
        ModuleFetch<ConquestOptimizationResult?> conquest = await conquestTask.ConfigureAwait(false);
        ModuleFetch<EraAnalysis?> era = await eraTask.ConfigureAwait(false);
        ModuleFetch<PlayerInventorySnapshot?> inventoryFetch = await inventoryTask.ConfigureAwait(false);
        PlayerInventorySnapshot? inventory = inventoryFetch.Succeeded ? inventoryFetch.Value : null;

        var signals = new List<InvestmentSignal>();
        var statuses = new List<InvestmentModuleStatus>(5);

        AddGacSignals(gac, signals, statuses);
        AddRiseOfEmpireSignals(rote, signals, statuses);
        AddConquestSignals(conquest, signals, statuses);
        AddEraSignals(era, signals, statuses);

        IReadOnlyCollection<InvestmentRecommendation> recommendations =
            InvestmentRecommendationBuilder.Build(signals, MaximumRecommendations, inventory);

        return new InvestmentOptimizationResult(
            player.AllyCode,
            player.Name,
            player.UpdatedAtUtc,
            clock.UtcNow,
            recommendations,
            statuses,
            inventory?.CapturedAtUtc,
            inventory?.Source);
    }

    private static void AddGacSignals(
        ModuleFetch<GacPlannerLookup> fetch,
        ICollection<InvestmentSignal> signals,
        ICollection<InvestmentModuleStatus> statuses)
    {
        if (!fetch.Succeeded || fetch.Value is null)
        {
            statuses.Add(Unavailable(InvestmentModule.Gac, fetch.Error));
            return;
        }

        GacPlannerLookup lookup = fetch.Value;
        if (!lookup.IsAvailable || lookup.State is null)
        {
            statuses.Add(new InvestmentModuleStatus(
                InvestmentModule.Gac,
                false,
                0,
                lookup.Message ?? "No hay una ronda GAC activa con rival disponible."));
            return;
        }

        int before = signals.Count;
        foreach (GacAttackAssignmentDetails attack in lookup.State.Plan.Attacks
                     .Where(attack => attack.Status == GacAttackPlanStatus.Planned))
        {
            foreach (GacPlannerUnitDetails unit in attack.Team.Squad.AllUnits)
            {
                signals.Add(new InvestmentSignal(
                    unit.DefinitionId,
                    unit.Name,
                    unit.ThumbnailName,
                    unit.RelicTier ?? 0,
                    0,
                    InvestmentModule.Gac,
                    17m,
                    $"Ataque activo: {attack.Team.Name} está reservado para el intento #{attack.Attempt}."));
            }
        }

        foreach (GacOwnDefenseAssignmentDetails defense in lookup.State.Plan.OwnDefenses)
        {
            foreach (GacPlannerUnitDetails unit in defense.Team.Squad.AllUnits)
            {
                signals.Add(new InvestmentSignal(
                    unit.DefinitionId,
                    unit.Name,
                    unit.ThumbnailName,
                    unit.RelicTier ?? 0,
                    0,
                    InvestmentModule.Gac,
                    13m,
                    $"Defensa activa: {defense.Team.Name} protege {defense.Zone}."));
            }
        }

        int count = signals.Count - before;
        statuses.Add(new InvestmentModuleStatus(
            InvestmentModule.Gac,
            true,
            count,
            count == 0
                ? "GAC disponible, pero aún no hay ataques o defensas planificados que aporten señales de inversión."
                : "Prioriza las unidades comprometidas en el plan real de la ronda; no inventa objetivos de reliquia."));
    }

    private static void AddRiseOfEmpireSignals(
        ModuleFetch<RiseOfEmpireAnalysis?> fetch,
        ICollection<InvestmentSignal> signals,
        ICollection<InvestmentModuleStatus> statuses)
    {
        if (!fetch.Succeeded || fetch.Value is null)
        {
            statuses.Add(Unavailable(InvestmentModule.RiseOfEmpire, fetch.Error ?? "No hay análisis de RotE disponible."));
            return;
        }

        int before = signals.Count;
        foreach (RiseOfEmpireUpgradePriority priority in fetch.Value.UpgradePriorities)
        {
            decimal score = Math.Max(14m, 42m - ((priority.Rank - 1) * 2.5m));
            signals.Add(new InvestmentSignal(
                priority.DefinitionId,
                priority.Name,
                priority.ThumbnailName,
                priority.CurrentRelicTier,
                0,
                InvestmentModule.RiseOfEmpire,
                score,
                $"{priority.Reason} Impacta {string.Join(", ", priority.Planets)}.",
                TargetRelicTier: priority.TargetRelicTier,
                ConcreteTarget: true));
        }

        int count = signals.Count - before;
        statuses.Add(new InvestmentModuleStatus(
            InvestmentModule.RiseOfEmpire,
            true,
            count,
            count == 0
                ? "RotE no detecta mejoras de reliquia pendientes en sus prioridades actuales."
                : "RotE aporta objetivos de reliquia concretos basados en equipos, misiones y desbloqueos."));
    }

    private static void AddConquestSignals(
        ModuleFetch<ConquestOptimizationResult?> fetch,
        ICollection<InvestmentSignal> signals,
        ICollection<InvestmentModuleStatus> statuses)
    {
        if (!fetch.Succeeded || fetch.Value is null)
        {
            statuses.Add(Unavailable(InvestmentModule.Conquest, fetch.Error ?? "No hay un plan activo de Conquista para optimizar."));
            return;
        }

        int before = signals.Count;
        foreach (ConquestTeamRecommendation recommendation in fetch.Value.Recommendations.Take(5))
        {
            decimal baseScore = Math.Max(4m, 12m - recommendation.Rank);
            decimal featBonus = Math.Min(4m, recommendation.AdvancesFeats.Count);
            foreach (ConquestOptimizationUnit unit in recommendation.Team)
            {
                signals.Add(new InvestmentSignal(
                    unit.DefinitionId,
                    unit.Name,
                    unit.ThumbnailName,
                    unit.RelicTier,
                    0,
                    InvestmentModule.Conquest,
                    baseScore + featBonus,
                    $"Aparece en la recomendación #{recommendation.Rank}, que avanza {recommendation.AdvancesFeats.Count} hazañas."));
            }
        }

        int count = signals.Count - before;
        statuses.Add(new InvestmentModuleStatus(
            InvestmentModule.Conquest,
            true,
            count,
            count == 0
                ? "Conquista está configurada, pero el optimizador no necesita equipos adicionales ahora mismo."
                : "Conquista puntúa la recurrencia en las mejores composiciones; stamina y discos no se convierten en reliquias ficticias."));
    }

    private static void AddEraSignals(
        ModuleFetch<EraAnalysis?> fetch,
        ICollection<InvestmentSignal> signals,
        ICollection<InvestmentModuleStatus> statuses)
    {
        if (!fetch.Succeeded || fetch.Value is null)
        {
            statuses.Add(Unavailable(InvestmentModule.Era, fetch.Error ?? "No hay análisis de Era disponible."));
            statuses.Add(Unavailable(InvestmentModule.Coliseum, fetch.Error ?? "No hay análisis de Coliseo disponible."));
            return;
        }

        EraAnalysis analysis = fetch.Value;
        int eraBefore = signals.Count;
        EraJourneyTierProgress? nextBlockedTier = analysis.Journey.Tiers
            .Where(tier => !tier.StarRequirementsMet)
            .OrderBy(tier => tier.Tier)
            .FirstOrDefault();
        if (nextBlockedTier is not null)
        {
            foreach (string requiredName in nextBlockedTier.RequiredUnits)
            {
                EraUnitStatus? unit = analysis.Units.FirstOrDefault(item =>
                    string.Equals(item.Name, requiredName, StringComparison.OrdinalIgnoreCase));
                if (unit is null || unit.Stars >= nextBlockedTier.RequiredStars)
                {
                    continue;
                }

                signals.Add(new InvestmentSignal(
                    unit.DefinitionId ?? $"ERA:{unit.Key}",
                    unit.Name,
                    unit.ThumbnailName,
                    unit.RelicTier,
                    unit.Stars,
                    InvestmentModule.Era,
                    34m,
                    $"Bloquea el siguiente paso del Journey de {analysis.Journey.UnitName}: tier {nextBlockedTier.Tier}.",
                    TargetStars: nextBlockedTier.RequiredStars,
                    ConcreteTarget: true));
            }
        }

        int eraCount = signals.Count - eraBefore;
        statuses.Add(new InvestmentModuleStatus(
            InvestmentModule.Era,
            true,
            eraCount,
            eraCount == 0
                ? "La siguiente barrera de estrellas del Journey de la Era está cubierta."
                : "Era aporta el siguiente requisito real de estrellas del Journey, incluidos personajes aún no desbloqueados."));

        int coliseumBefore = signals.Count;
        foreach (EraUnitStatus unit in analysis.Units.Where(unit => unit.Owned && !unit.IsJourneyUnit))
        {
            signals.Add(new InvestmentSignal(
                unit.DefinitionId!,
                unit.Name,
                unit.ThumbnailName,
                unit.RelicTier,
                unit.Stars,
                InvestmentModule.Coliseum,
                6m,
                "Unidad propia de la Era actual utilizable en Coliseo; suma valor transversal sin inferir Era Level."));
        }

        int coliseumCount = signals.Count - coliseumBefore;
        statuses.Add(new InvestmentModuleStatus(
            InvestmentModule.Coliseum,
            true,
            coliseumCount,
            coliseumCount == 0
                ? "No hay unidades propias de la Era actual disponibles para puntuar en Coliseo."
                : "Coliseo añade valor estratégico, pero no fija objetivos de Era Level porque ese dato no está disponible en el roster."));
    }

    private static InvestmentModuleStatus Unavailable(InvestmentModule module, string? message) => new(
        module,
        false,
        0,
        string.IsNullOrWhiteSpace(message) ? "Módulo no disponible para esta optimización." : message);

    private static async Task<ModuleFetch<T>> CaptureAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return new ModuleFetch<T>(true, await action().ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ModuleFetch<T>(false, default, exception.Message);
        }
    }

    private sealed record ModuleFetch<T>(bool Succeeded, T? Value, string? Error);
}
