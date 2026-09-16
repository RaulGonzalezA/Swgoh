using Swgoh.Application.TerritoryBattles;

using Xunit;

namespace Swgoh.Application.UnitTests.TerritoryBattles;

public sealed class RiseOfEmpireExecutionProgressTests
{
    [Fact]
    public void Build_SeparatesPendingInProgressAndRosterGaps()
    {
        RiseOfEmpireGuildAnalysis guild = Guild();
        var session = new RiseOfEmpireExecutionSession(
            "session-1",
            "guild-1",
            "Guild",
            "TB actual",
            RiseOfEmpireExecutionStatus.Active,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            [
                Result(101, "Uno", "m1", RiseOfEmpireMissionExecutionState.Finished, 100),
                Result(102, "Dos", "m1", RiseOfEmpireMissionExecutionState.InProgress, 50)
            ]);

        RiseOfEmpireExecutionProgress progress = RiseOfEmpireExecutionProgressBuilder.Build(guild, session);

        Assert.Equal(4, progress.TargetAttempts);
        Assert.Equal(3, progress.PlannedAttempts);
        Assert.Equal(1, progress.FinishedAttempts);
        Assert.Equal(1, progress.InProgressAttempts);
        Assert.Equal(1, progress.PendingAttempts);
        Assert.Equal(1, progress.RosterGapAttempts);
        Assert.Equal(150, progress.RecordedTerritoryPoints);
        Assert.Equal(33.3m, progress.CompletionPercent);

        RiseOfEmpireExecutionMissionProgress first = Assert.Single(progress.Phases.Single().Missions, mission => mission.MissionId == "m1");
        Assert.Equal(2, first.PlannedAttempts);
        Assert.Equal(1, first.FinishedAttempts);
        Assert.Equal(1, first.InProgressAttempts);
        Assert.Equal(0, first.PendingAttempts);
        Assert.Contains("Dos", first.InProgressMembers);

        RiseOfEmpireExecutionMissionProgress second = Assert.Single(progress.Phases.Single().Missions, mission => mission.MissionId == "m2");
        Assert.Equal(2, second.TargetAttempts);
        Assert.Equal(1, second.PlannedAttempts);
        Assert.Equal(1, second.PendingAttempts);
        Assert.Equal(1, second.RosterGapAttempts);
        Assert.Contains("Uno", second.PendingMembers);
    }

    [Fact]
    public void Build_TreatsExplicitNotAttemptedAsPending()
    {
        RiseOfEmpireGuildAnalysis guild = Guild();
        var session = new RiseOfEmpireExecutionSession(
            "session-1",
            "guild-1",
            "Guild",
            "TB actual",
            RiseOfEmpireExecutionStatus.Active,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            [Result(101, "Uno", "m1", RiseOfEmpireMissionExecutionState.NotAttempted, 0)]);

        RiseOfEmpireExecutionProgress progress = RiseOfEmpireExecutionProgressBuilder.Build(guild, session);

        RiseOfEmpireExecutionMemberProgress member = Assert.Single(
            progress.Members,
            item => item.AllyCode == 101 && item.Phase == 1);
        Assert.Equal(2, member.PendingAttempts);
        Assert.Equal(0, member.FinishedAttempts);
        Assert.Equal(0, member.InProgressAttempts);
    }

    private static RiseOfEmpireGuildAnalysis Guild()
    {
        RiseOfEmpireGuildMissionMember oneM1 = Member(101, "Uno", "Team 1");
        RiseOfEmpireGuildMissionMember twoM1 = Member(102, "Dos", "Team 1");
        RiseOfEmpireGuildMissionMember oneM2 = Member(101, "Uno", "Team 2");

        return new RiseOfEmpireGuildAnalysis(
            "guild-1",
            "Guild",
            20_000_000,
            2,
            2,
            DateTimeOffset.UtcNow,
            [],
            [],
            [],
            [],
            [
                Coverage("m1", "Misión uno", [oneM1, twoM1]),
                Coverage("m2", "Misión dos", [oneM2])
            ],
            [],
            [
                new RiseOfEmpireGuildMemberOperationalPlan(
                    101,
                    "Uno",
                    [new RiseOfEmpireGuildMemberPhaseOperationalPlan(
                        1,
                        [Attempt(1, "m1", "Misión uno"), Attempt(2, "m2", "Misión dos")],
                        [],
                        [],
                        [])]),
                new RiseOfEmpireGuildMemberOperationalPlan(
                    102,
                    "Dos",
                    [new RiseOfEmpireGuildMemberPhaseOperationalPlan(
                        1,
                        [Attempt(1, "m1", "Misión uno")],
                        [],
                        [],
                        [])])
            ]);
    }

    private static RiseOfEmpireGuildMissionCoverage Coverage(
        string missionId,
        string missionName,
        IReadOnlyCollection<RiseOfEmpireGuildMissionMember> planned) => new(
        1,
        "planet",
        "Planeta",
        missionId,
        missionName,
        "Combat",
        false,
        2,
        planned.Count,
        planned,
        planned,
        [],
        []);

    private static RiseOfEmpireGuildMissionMember Member(long allyCode, string name, string team) => new(
        allyCode,
        name,
        team,
        true,
        5,
        5,
        []);

    private static RiseOfEmpireMemberMissionAttemptPlan Attempt(int order, string missionId, string missionName) => new(
        order,
        "planet",
        "Planeta",
        missionId,
        missionName,
        $"Team {order}",
        []);

    private static RiseOfEmpireMissionExecutionResult Result(
        long allyCode,
        string playerName,
        string missionId,
        RiseOfEmpireMissionExecutionState state,
        long points) => new(
        allyCode,
        playerName,
        1,
        "planet",
        "Planeta",
        missionId,
        missionId == "m1" ? "Misión uno" : "Misión dos",
        "Team",
        state,
        state == RiseOfEmpireMissionExecutionState.Finished ? 2 : 0,
        2,
        points,
        null,
        DateTimeOffset.UtcNow);
}
