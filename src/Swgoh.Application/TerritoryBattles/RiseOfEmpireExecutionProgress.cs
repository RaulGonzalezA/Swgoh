namespace Swgoh.Application.TerritoryBattles;

public sealed record RiseOfEmpireExecutionProgress(
    string SessionId,
    string Label,
    int TargetAttempts,
    int PlannedAttempts,
    int FinishedAttempts,
    int InProgressAttempts,
    int PendingAttempts,
    int RosterGapAttempts,
    long RecordedTerritoryPoints,
    IReadOnlyCollection<RiseOfEmpireExecutionPhaseProgress> Phases,
    IReadOnlyCollection<RiseOfEmpireExecutionMemberProgress> Members)
{
    public decimal CompletionPercent => PlannedAttempts == 0
        ? 0m
        : Math.Round(FinishedAttempts * 100m / PlannedAttempts, 1);
}

public sealed record RiseOfEmpireExecutionPhaseProgress(
    int Phase,
    int TargetAttempts,
    int PlannedAttempts,
    int FinishedAttempts,
    int InProgressAttempts,
    int PendingAttempts,
    int RosterGapAttempts,
    long RecordedTerritoryPoints,
    IReadOnlyCollection<RiseOfEmpireExecutionMissionProgress> Missions);

public sealed record RiseOfEmpireExecutionMissionProgress(
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

public sealed record RiseOfEmpireExecutionMemberProgress(
    long AllyCode,
    string PlayerName,
    int Phase,
    int PlannedAttempts,
    int FinishedAttempts,
    int InProgressAttempts,
    int PendingAttempts,
    IReadOnlyCollection<string> PendingMissions);

public interface IRiseOfEmpireExecutionProgressService
{
    Task<RiseOfEmpireExecutionProgress?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default);
}

internal sealed class RiseOfEmpireExecutionProgressService(
    IRiseOfEmpireGuildService guildService,
    IRiseOfEmpireExecutionService executionService) : IRiseOfEmpireExecutionProgressService
{
    public async Task<RiseOfEmpireExecutionProgress?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        Task<RiseOfEmpireGuildAnalysis?> guildTask = guildService.GetAsync(
            allyCode,
            refreshGuildRoster: false,
            cancellationToken);
        Task<RiseOfEmpireExecutionSession?> executionTask = executionService.GetActiveAsync(
            allyCode,
            cancellationToken);
        await Task.WhenAll(guildTask, executionTask).ConfigureAwait(false);

        RiseOfEmpireGuildAnalysis? guild = await guildTask.ConfigureAwait(false);
        RiseOfEmpireExecutionSession? session = await executionTask.ConfigureAwait(false);
        return guild is null || session is null
            ? null
            : RiseOfEmpireExecutionProgressBuilder.Build(guild, session);
    }
}

internal static class RiseOfEmpireExecutionProgressBuilder
{
    public static RiseOfEmpireExecutionProgress Build(
        RiseOfEmpireGuildAnalysis guild,
        RiseOfEmpireExecutionSession session)
    {
        ArgumentNullException.ThrowIfNull(guild);
        ArgumentNullException.ThrowIfNull(session);

        Dictionary<string, RiseOfEmpireMissionExecutionResult> results = session.Results
            .ToDictionary(result => result.Key, StringComparer.OrdinalIgnoreCase);
        RiseOfEmpireExecutionMissionProgress[] missions =
        [
            .. guild.MissionCoverage
                .Select(mission => BuildMission(mission, results))
                .OrderBy(mission => mission.Phase)
                .ThenBy(mission => mission.PlanetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mission => mission.MissionName, StringComparer.OrdinalIgnoreCase)
        ];
        RiseOfEmpireExecutionPhaseProgress[] phases =
        [
            .. missions
                .GroupBy(mission => mission.Phase)
                .OrderBy(group => group.Key)
                .Select(BuildPhase)
        ];
        RiseOfEmpireExecutionMemberProgress[] members = BuildMembers(guild, results);

        return new RiseOfEmpireExecutionProgress(
            session.Id,
            session.Label,
            missions.Sum(mission => mission.TargetAttempts),
            missions.Sum(mission => mission.PlannedAttempts),
            missions.Sum(mission => mission.FinishedAttempts),
            missions.Sum(mission => mission.InProgressAttempts),
            missions.Sum(mission => mission.PendingAttempts),
            missions.Sum(mission => mission.RosterGapAttempts),
            missions.Sum(mission => mission.RecordedTerritoryPoints),
            phases,
            members);
    }

    private static RiseOfEmpireExecutionMissionProgress BuildMission(
        RiseOfEmpireGuildMissionCoverage mission,
        IReadOnlyDictionary<string, RiseOfEmpireMissionExecutionResult> results)
    {
        int finished = 0;
        int inProgress = 0;
        long territoryPoints = 0;
        var pendingMembers = new List<string>();
        var inProgressMembers = new List<string>();

        foreach (RiseOfEmpireGuildMissionMember member in mission.PlannedMembers)
        {
            string key = RiseOfEmpireMissionExecutionResult.KeyFor(
                member.AllyCode,
                mission.Phase,
                mission.PlanetId,
                mission.MissionId);
            if (!results.TryGetValue(key, out RiseOfEmpireMissionExecutionResult? result)
                || result.State == RiseOfEmpireMissionExecutionState.NotAttempted)
            {
                pendingMembers.Add(member.PlayerName);
                continue;
            }

            territoryPoints += result.TerritoryPoints ?? 0;
            if (result.State == RiseOfEmpireMissionExecutionState.Finished)
            {
                finished++;
            }
            else
            {
                inProgress++;
                inProgressMembers.Add(member.PlayerName);
            }
        }

        int planned = mission.PlannedMembers.Count;
        return new RiseOfEmpireExecutionMissionProgress(
            mission.Phase,
            mission.PlanetId,
            mission.PlanetName,
            mission.MissionId,
            mission.MissionName,
            mission.IsFleet,
            mission.TargetAttempts,
            planned,
            finished,
            inProgress,
            Math.Max(0, planned - finished - inProgress),
            Math.Max(0, mission.TargetAttempts - planned),
            territoryPoints,
            [.. pendingMembers.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)],
            [.. inProgressMembers.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)]);
    }

    private static RiseOfEmpireExecutionPhaseProgress BuildPhase(
        IGrouping<int, RiseOfEmpireExecutionMissionProgress> group)
    {
        RiseOfEmpireExecutionMissionProgress[] missions = [.. group];
        return new RiseOfEmpireExecutionPhaseProgress(
            group.Key,
            missions.Sum(mission => mission.TargetAttempts),
            missions.Sum(mission => mission.PlannedAttempts),
            missions.Sum(mission => mission.FinishedAttempts),
            missions.Sum(mission => mission.InProgressAttempts),
            missions.Sum(mission => mission.PendingAttempts),
            missions.Sum(mission => mission.RosterGapAttempts),
            missions.Sum(mission => mission.RecordedTerritoryPoints),
            missions);
    }

    private static RiseOfEmpireExecutionMemberProgress[] BuildMembers(
        RiseOfEmpireGuildAnalysis guild,
        IReadOnlyDictionary<string, RiseOfEmpireMissionExecutionResult> results) =>
    [
        .. guild.MemberPlans
            .SelectMany(member => member.Phases.Select(phase => BuildMember(member, phase, results)))
            .OrderBy(member => member.Phase)
            .ThenByDescending(member => member.PendingAttempts)
            .ThenBy(member => member.PlayerName, StringComparer.OrdinalIgnoreCase)
    ];

    private static RiseOfEmpireExecutionMemberProgress BuildMember(
        RiseOfEmpireGuildMemberOperationalPlan member,
        RiseOfEmpireGuildMemberPhaseOperationalPlan phase,
        IReadOnlyDictionary<string, RiseOfEmpireMissionExecutionResult> results)
    {
        int finished = 0;
        int inProgress = 0;
        var pending = new List<string>();

        foreach (RiseOfEmpireMemberMissionAttemptPlan attempt in phase.MissionAttempts)
        {
            string key = RiseOfEmpireMissionExecutionResult.KeyFor(
                member.AllyCode,
                phase.Phase,
                attempt.PlanetId,
                attempt.MissionId);
            if (!results.TryGetValue(key, out RiseOfEmpireMissionExecutionResult? result)
                || result.State == RiseOfEmpireMissionExecutionState.NotAttempted)
            {
                pending.Add($"{attempt.PlanetName} · {attempt.MissionName}");
                continue;
            }

            if (result.State == RiseOfEmpireMissionExecutionState.Finished)
            {
                finished++;
            }
            else
            {
                inProgress++;
            }
        }

        int planned = phase.MissionAttempts.Count;
        return new RiseOfEmpireExecutionMemberProgress(
            member.AllyCode,
            member.PlayerName,
            phase.Phase,
            planned,
            finished,
            inProgress,
            Math.Max(0, planned - finished - inProgress),
            pending);
    }
}
