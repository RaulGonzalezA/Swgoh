using Microsoft.AspNetCore.Components;

using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class Conquest
{
    [Inject]
    private ConquestDailyPlanApiClient DailyPlanClient { get; set; } = null!;

    protected bool PlanningDay { get; private set; }
    protected int DailyPlanBattles { get; set; } = 6;
    protected ConquestDailyPlanApiClient.DailyPlanViewModel? DailyPlan { get; private set; }

    protected async Task BuildDailyPlanAsync()
    {
        PlanningDay = true;
        Error = null;
        try
        {
            DailyPlanBattles = Math.Clamp(DailyPlanBattles, 1, 20);
            await PersistPlanAsync();
            DailyPlan = await DailyPlanClient.BuildAsync(AllyCode, DailyPlanBattles);
            if (DailyPlan is null)
            {
                Error = "Guarda primero una Conquista con hazañas pendientes.";
            }
        }
        catch (HttpRequestException)
        {
            Error = "No se ha podido generar el plan diario de Conquista.";
        }
        finally
        {
            PlanningDay = false;
        }
    }

    protected static string DailyStopReason(string value) => value switch
    {
        "RewardTargetReached" => "Se alcanzó el objetivo de recompensa configurado",
        "EnergyBudgetExhausted" => "No queda energía suficiente para otro combate",
        "AllFeatsCompleted" => "Todas las hazañas proyectadas quedan completadas",
        "NoUsableCharacters" => "No quedan personajes utilizables con la stamina actual",
        "NoViableTeam" => "No hay otro equipo que avance las hazañas pendientes",
        "RosterUnavailable" => "El roster no está disponible",
        _ => "Se alcanzó el límite de combates planificados"
    };
}
