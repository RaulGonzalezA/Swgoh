using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class GacAttackPlanner
{
    protected GacPlannerApiClient.OptimizationViewModel? Optimization { get; private set; }
    protected bool OptimizerBusy { get; private set; }
    protected string? OptimizerError { get; private set; }

    protected Task PreviewFillGapsAsync() => RunOptimizationAsync("FillGaps", apply: false);

    protected Task PreviewRebuildAsync() => RunOptimizationAsync("RebuildPlanned", apply: false);

    protected Task ApplyOptimizationAsync()
    {
        string mode = Optimization?.Mode ?? "FillGaps";
        return RunOptimizationAsync(mode, apply: true);
    }

    protected static string FriendlyOptimizationMode(string mode) => mode switch
    {
        "FillGaps" => "Completar huecos",
        "RebuildPlanned" => "Reoptimizar pendientes",
        _ => mode
    };

    protected static string OptimizerEvidenceClass(string evidence) =>
        evidence.Contains("Histórico", StringComparison.OrdinalIgnoreCase)
            ? "optimizer-evidence-history"
            : evidence.Contains("Counter", StringComparison.OrdinalIgnoreCase)
                ? "optimizer-evidence-counter"
                : "optimizer-evidence-heuristic";

    private async Task RunOptimizationAsync(string mode, bool apply)
    {
        OptimizerBusy = true;
        OptimizerError = null;
        try
        {
            GacPlannerApiClient.OptimizationResult result = await PlannerClient.OptimizeCurrentAsync(
                AllyCode,
                mode,
                apply);
            if (result.Envelope is null)
            {
                OptimizerError = result.Message ?? "No se ha podido generar una propuesta de ataques.";
                return;
            }

            Planner = result.Envelope.Planner;
            Optimization = result.Envelope.Optimization;
            if (Optimization.Applied)
            {
                MapDraftsFromPlanner();
            }
        }
        catch (HttpRequestException)
        {
            OptimizerError = "No se ha podido ejecutar el optimizador. Comprueba que la API está disponible.";
        }
        finally
        {
            OptimizerBusy = false;
        }
    }
}
