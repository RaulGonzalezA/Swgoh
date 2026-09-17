using System.Net;
using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class GacAttackExecutionApiClient(HttpClient httpClient)
{
    public async Task<ExecutionResult> ExecuteAsync(
        long allyCode,
        Guid attackId,
        string status,
        int? banners,
        string? notes,
        IReadOnlyCollection<string>? remainingEnemyUnitDefinitionIds = null,
        bool preloadedTurnMeter = false,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/v1/gac/players/{allyCode}/planner/current/attacks/{attackId}/result",
            new ExecuteAttackRequest(
                status,
                banners,
                notes,
                remainingEnemyUnitDefinitionIds,
                preloadedTurnMeter),
            cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            ExecutionEnvelopeViewModel? envelope =
                await response.Content.ReadFromJsonAsync<ExecutionEnvelopeViewModel>(cancellationToken);
            return new ExecutionResult(envelope, null);
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            GacPlannerApiClient.PlannerUnavailableViewModel? unavailable =
                await response.Content.ReadFromJsonAsync<GacPlannerApiClient.PlannerUnavailableViewModel>(cancellationToken);
            return new ExecutionResult(null, unavailable?.Message ?? "No hay una ronda de Gran Arena disponible.");
        }

        response.EnsureSuccessStatusCode();
        return new ExecutionResult(null, "No se ha podido registrar el resultado del ataque.");
    }

    public sealed record ExecutionResult(ExecutionEnvelopeViewModel? Envelope, string? Message);

    public sealed record ExecuteAttackRequest(
        string Status,
        int? Banners,
        string? Notes,
        IReadOnlyCollection<string>? RemainingEnemyUnitDefinitionIds = null,
        bool PreloadedTurnMeter = false);

    public sealed record ExecutionEnvelopeViewModel(
        GacPlannerApiClient.PlannerViewModel Planner,
        GacPlannerApiClient.OptimizationViewModel? Optimization,
        ExecutedAttackViewModel Execution,
        GacPlannerApiClient.OptimizationRecommendationViewModel? NextRecommendation,
        IReadOnlyCollection<string>? Warnings = null)
    {
        public IReadOnlyCollection<string> PostCommitWarnings => Warnings ?? [];
    }

    public sealed record ExecutedAttackViewModel(
        Guid AttackId,
        string Status,
        int? Banners,
        string? Notes,
        IReadOnlyCollection<string>? RemainingEnemyUnitDefinitionIds = null,
        bool PreloadedTurnMeter = false,
        bool IsCleanup = false)
    {
        public IReadOnlyCollection<string> EnemySurvivors => RemainingEnemyUnitDefinitionIds ?? [];
    }
}
