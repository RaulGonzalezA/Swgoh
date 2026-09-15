using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.Caching.Memory;

namespace Swgoh.Blazor.Clients;

public sealed class GacPlannerPerformanceApiClient(HttpClient httpClient, IMemoryCache cache)
{
    private static readonly TimeSpan ContextCacheLifetime = TimeSpan.FromMinutes(10);

    public bool TryGetCachedContext(long allyCode, out PlannerContextViewModel? context) =>
        cache.TryGetValue(ContextCacheKey(allyCode), out context) && context is not null;

    public async Task<PlannerContextResult> GetContextAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < 210; attempt++)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                $"/api/v1/gac/players/{allyCode}/planner/context",
                cancellationToken);
            if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.Conflict)
            {
                GacPlannerApiClient.PlannerUnavailableViewModel? status =
                    await response.Content.ReadFromJsonAsync<GacPlannerApiClient.PlannerUnavailableViewModel>(cancellationToken);
                if (status?.Status == "Pending")
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                return new PlannerContextResult(
                    null,
                    status?.Message ?? "No hay una ronda de Gran Arena disponible.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                GacPlannerApiClient.PlannerUnavailableViewModel? unavailable =
                    await response.Content.ReadFromJsonAsync<GacPlannerApiClient.PlannerUnavailableViewModel>(cancellationToken);
                return new PlannerContextResult(
                    null,
                    unavailable?.Message ?? "No hay una ronda de Gran Arena disponible.");
            }

            response.EnsureSuccessStatusCode();
            PlannerContextViewModel? context =
                await response.Content.ReadFromJsonAsync<PlannerContextViewModel>(cancellationToken);
            if (context is null)
            {
                return new PlannerContextResult(null, "La API devolvió un contexto GAC vacío.");
            }

            cache.Set(ContextCacheKey(allyCode), context, ContextCacheLifetime);
            return new PlannerContextResult(context, null);
        }

        return new PlannerContextResult(
            null,
            "La búsqueda continúa en segundo plano. Puedes volver a consultar más tarde.");
    }

    public Task<GacPlannerApiClient.PlannerResult> AddOwnDefenseAsync(
        long allyCode,
        string zone,
        Guid teamPresetId,
        DateTimeOffset expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SendPlannerMutationAsync(
            allyCode,
            HttpMethod.Post,
            $"/api/v1/gac/players/{allyCode}/planner/current/own-defenses",
            new OwnDefenseMutationRequest(zone, teamPresetId, expectedUpdatedAtUtc),
            cancellationToken);

    public Task<GacPlannerApiClient.PlannerResult> RemoveOwnDefenseAsync(
        long allyCode,
        Guid assignmentId,
        DateTimeOffset expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SendPlannerMutationAsync(
            allyCode,
            HttpMethod.Delete,
            $"/api/v1/gac/players/{allyCode}/planner/current/own-defenses/{assignmentId}?expectedUpdatedAtUtc={EscapeTimestamp(expectedUpdatedAtUtc)}",
            body: null,
            cancellationToken);

    public Task<GacPlannerApiClient.PlannerResult> AddVisibleDefenseAsync(
        long allyCode,
        VisibleDefenseMutationRequest request,
        CancellationToken cancellationToken = default) =>
        SendPlannerMutationAsync(
            allyCode,
            HttpMethod.Post,
            $"/api/v1/gac/players/{allyCode}/planner/current/visible-defenses",
            request,
            cancellationToken);

    public Task<GacPlannerApiClient.PlannerResult> RemoveVisibleDefenseAsync(
        long allyCode,
        Guid defenseId,
        DateTimeOffset expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SendPlannerMutationAsync(
            allyCode,
            HttpMethod.Delete,
            $"/api/v1/gac/players/{allyCode}/planner/current/visible-defenses/{defenseId}?expectedUpdatedAtUtc={EscapeTimestamp(expectedUpdatedAtUtc)}",
            body: null,
            cancellationToken);

    public Task<GacPlannerApiClient.PlannerResult> AddAttackAsync(
        long allyCode,
        Guid defenseId,
        Guid teamPresetId,
        string? notes,
        DateTimeOffset expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SendPlannerMutationAsync(
            allyCode,
            HttpMethod.Post,
            $"/api/v1/gac/players/{allyCode}/planner/current/attacks",
            new AddAttackMutationRequest(defenseId, teamPresetId, notes, expectedUpdatedAtUtc),
            cancellationToken);

    public Task<GacPlannerApiClient.PlannerResult> UpdateAttackAsync(
        long allyCode,
        Guid attackId,
        string status,
        string? notes,
        DateTimeOffset expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SendPlannerMutationAsync(
            allyCode,
            HttpMethod.Patch,
            $"/api/v1/gac/players/{allyCode}/planner/current/attacks/{attackId}",
            new UpdateAttackMutationRequest(status, notes, expectedUpdatedAtUtc),
            cancellationToken);

    private async Task<GacPlannerApiClient.PlannerResult> SendPlannerMutationAsync(
        long allyCode,
        HttpMethod method,
        string requestUri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        GacPlannerApiClient.PlannerResult result = await ReadPlannerResultAsync(response, cancellationToken);
        if (result.Planner is not null && TryGetCachedContext(allyCode, out PlannerContextViewModel? cached) && cached is not null)
        {
            cache.Set(
                ContextCacheKey(allyCode),
                cached with { Planner = result.Planner },
                ContextCacheLifetime);
        }

        return result;
    }

    private static async Task<GacPlannerApiClient.PlannerResult> ReadPlannerResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            GacPlannerApiClient.PlannerViewModel? planner =
                await response.Content.ReadFromJsonAsync<GacPlannerApiClient.PlannerViewModel>(cancellationToken);
            return new GacPlannerApiClient.PlannerResult(planner, null);
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            GacPlannerApiClient.PlannerUnavailableViewModel? unavailable =
                await response.Content.ReadFromJsonAsync<GacPlannerApiClient.PlannerUnavailableViewModel>(cancellationToken);
            return new GacPlannerApiClient.PlannerResult(
                null,
                unavailable?.Message ?? "No se ha podido actualizar el plan de Gran Arena.");
        }

        response.EnsureSuccessStatusCode();
        return new GacPlannerApiClient.PlannerResult(null, "No se ha podido actualizar el plan de Gran Arena.");
    }

    private static string ContextCacheKey(long allyCode) => $"gac-planner-context:{allyCode}";

    private static string EscapeTimestamp(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToString("O"));

    public sealed record PlannerContextResult(PlannerContextViewModel? Context, string? Message);

    public sealed record PlannerContextViewModel(
        GacPlannerApiClient.PlannerViewModel Planner,
        RosterSnapshotViewModel? PlayerRoster,
        RosterSnapshotViewModel? OpponentRoster);

    public sealed record RosterSnapshotViewModel(
        long AllyCode,
        DateTimeOffset UpdatedAtUtc,
        string? PlayerName,
        long GalacticPower,
        int RosterCount,
        IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> Items,
        IReadOnlyCollection<string> AvailableFactions);

    public sealed record OwnDefenseMutationRequest(
        string Zone,
        Guid TeamPresetId,
        DateTimeOffset ExpectedUpdatedAtUtc);

    public sealed record VisibleDefenseMutationRequest(
        string Zone,
        string? Label,
        string LeaderDefinitionId,
        IReadOnlyCollection<string> MemberDefinitionIds,
        bool IsFleet,
        DateTimeOffset ExpectedUpdatedAtUtc);

    public sealed record AddAttackMutationRequest(
        Guid DefenseId,
        Guid TeamPresetId,
        string? Notes,
        DateTimeOffset ExpectedUpdatedAtUtc);

    public sealed record UpdateAttackMutationRequest(
        string Status,
        string? Notes,
        DateTimeOffset ExpectedUpdatedAtUtc);
}
