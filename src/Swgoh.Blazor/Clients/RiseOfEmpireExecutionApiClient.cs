using System.Net;
using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class RiseOfEmpireExecutionApiClient(HttpClient httpClient)
{
    public async Task<RiseOfEmpireExecutionSessionViewModel?> GetActiveAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(Base(allyCode), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiseOfEmpireExecutionSessionViewModel>(cancellationToken);
    }

    public async Task<RiseOfEmpireExecutionProgressViewModel?> GetProgressAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync($"{Base(allyCode)}/progress", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiseOfEmpireExecutionProgressViewModel>(cancellationToken);
    }

    public async Task<IReadOnlyCollection<RiseOfEmpireExecutionSessionViewModel>> GetHistoryAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync($"{Base(allyCode)}/history", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiseOfEmpireExecutionSessionViewModel[]>(cancellationToken) ?? [];
    }

    public async Task<RiseOfEmpireExecutionSessionViewModel> StartAsync(
        long allyCode,
        string? label,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            Base(allyCode),
            new StartExecutionRequest(label),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync(response, cancellationToken);
    }

    public async Task<RiseOfEmpireExecutionSessionViewModel> UpdateMissionAsync(
        long allyCode,
        string sessionId,
        RiseOfEmpireMissionResultRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"{Base(allyCode)}/{Uri.EscapeDataString(sessionId)}/missions",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync(response, cancellationToken);
    }

    public async Task<RiseOfEmpireExecutionSessionViewModel> CloseAsync(
        long allyCode,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
            $"{Base(allyCode)}/{Uri.EscapeDataString(sessionId)}/close",
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync(response, cancellationToken);
    }

    private static string Base(long allyCode) =>
        $"/api/v1/territory-battles/players/{allyCode}/rise-of-the-empire/guild/execution";

    private static async Task<RiseOfEmpireExecutionSessionViewModel> ReadRequiredAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<RiseOfEmpireExecutionSessionViewModel>(cancellationToken)
        ?? throw new HttpRequestException("The RotE execution response was empty.");

    private sealed record StartExecutionRequest(string? Label);

    public enum RiseOfEmpireExecutionStatusViewModel
    {
        Active = 0,
        Closed = 1
    }

    public enum RiseOfEmpireMissionExecutionStateViewModel
    {
        NotAttempted = 0,
        InProgress = 1,
        Finished = 2
    }

    public sealed record RiseOfEmpireExecutionSessionViewModel(
        string Id,
        string GuildId,
        string GuildName,
        string Label,
        RiseOfEmpireExecutionStatusViewModel Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? ClosedAtUtc,
        IReadOnlyCollection<RiseOfEmpireMissionExecutionResultViewModel> Results,
        int FinishedAttempts,
        int InProgressAttempts,
        long RecordedTerritoryPoints);

    public sealed record RiseOfEmpireMissionExecutionResultViewModel(
        long PlayerAllyCode,
        string PlayerName,
        int Phase,
        string PlanetId,
        string PlanetName,
        string MissionId,
        string MissionName,
        string? TeamName,
        RiseOfEmpireMissionExecutionStateViewModel State,
        int CompletedWaves,
        int TotalWaves,
        long? TerritoryPoints,
        string? Notes,
        DateTimeOffset UpdatedAtUtc,
        string Key);

    public sealed record RiseOfEmpireExecutionProgressViewModel(
        string SessionId,
        string Label,
        int TargetAttempts,
        int PlannedAttempts,
        int FinishedAttempts,
        int InProgressAttempts,
        int PendingAttempts,
        int RosterGapAttempts,
        long RecordedTerritoryPoints,
        IReadOnlyCollection<RiseOfEmpireExecutionPhaseProgressViewModel> Phases,
        IReadOnlyCollection<RiseOfEmpireExecutionMemberProgressViewModel> Members,
        decimal CompletionPercent);

    public sealed record RiseOfEmpireExecutionPhaseProgressViewModel(
        int Phase,
        int TargetAttempts,
        int PlannedAttempts,
        int FinishedAttempts,
        int InProgressAttempts,
        int PendingAttempts,
        int RosterGapAttempts,
        long RecordedTerritoryPoints,
        IReadOnlyCollection<RiseOfEmpireExecutionMissionProgressViewModel> Missions);

    public sealed record RiseOfEmpireExecutionMissionProgressViewModel(
        int Phase,
        string PlanetId,
        string PlanetName,
        string MissionId,
        string MissionName,
        bool IsFleet,
        int TargetAttempts,
        int PlannedAttempts,
        int FinishedAttempts,
        int InProgressAttempts,
        int PendingAttempts,
        int RosterGapAttempts,
        long RecordedTerritoryPoints,
        IReadOnlyCollection<string> PendingMembers,
        IReadOnlyCollection<string> InProgressMembers);

    public sealed record RiseOfEmpireExecutionMemberProgressViewModel(
        long AllyCode,
        string PlayerName,
        int Phase,
        int PlannedAttempts,
        int FinishedAttempts,
        int InProgressAttempts,
        int PendingAttempts,
        IReadOnlyCollection<string> PendingMissions);

    public sealed record RiseOfEmpireMissionResultRequest(
        long PlayerAllyCode,
        string PlayerName,
        int Phase,
        string PlanetId,
        string PlanetName,
        string MissionId,
        string MissionName,
        string? TeamName,
        RiseOfEmpireMissionExecutionStateViewModel State,
        int CompletedWaves,
        int TotalWaves,
        long? TerritoryPoints,
        string? Notes);
}
