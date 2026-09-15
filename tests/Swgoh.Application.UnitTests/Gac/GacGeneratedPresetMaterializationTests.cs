using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacGeneratedPresetMaterializationTests
{
    private const long AllyCode = 123_456_789;

    [Fact]
    public async Task MaterializeAsync_WhenLaterCreateIsCancelled_RollsBackAlreadyCreatedPresetsWithIndependentToken()
    {
        using var requestCancellation = new CancellationTokenSource();
        var planner = new FailingPlanner(requestCancellation);
        GacTeamPresetDetails first = Preset(Guid.NewGuid(), "Auto 1", "L1");
        GacTeamPresetDetails second = Preset(Guid.NewGuid(), "Auto 2", "L2");
        var candidates = new GacRosterDefenseCandidateSet(
            [first, second],
            new Dictionary<Guid, GacTeamPresetDetails>
            {
                [first.Id] = first,
                [second.Id] = second
            },
            []);
        GacSmartDefenseAssignment[] assignments =
        [
            Assignment(1, first),
            Assignment(2, second)
        ];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GacRosterDefenseCandidateService.MaterializeAsync(
                planner,
                AllyCode,
                GacFormat.FiveVsFive,
                assignments,
                candidates,
                requestCancellation.Token));

        Guid created = Assert.Single(planner.CreatedPresetIds);
        Assert.Equal(created, Assert.Single(planner.DeletedPresetIds));
        Assert.All(planner.DeleteTokenWasCancelled, Assert.False);
        Assert.True(requestCancellation.IsCancellationRequested);
    }

    private static GacSmartDefenseAssignment Assignment(int position, GacTeamPresetDetails preset) => new(
        position,
        "Norte frontal",
        preset.Id,
        preset.Name,
        Pinned: false,
        IsFleet: false,
        GalacticPower: 100_000,
        Score: 80m,
        DefensiveValue: 80m,
        OffensiveOpportunityCost: 0m,
        Confidence: "Medium",
        ContainsGalacticLegend: false,
        OmicronCount: 0,
        EligibleDatacronTier: 0,
        OpponentSamples: 0,
        PersonalSamples: 0,
        Reasons: []);

    private static GacTeamPresetDetails Preset(Guid id, string name, string leaderId) => new(
        id,
        AllyCode,
        name,
        GacFormat.FiveVsFive,
        GacPlannerTeamUse.Defense,
        new GacPlannerSquadDetails(
            Unit(leaderId),
            [Unit($"{leaderId}-M1")],
            IsFleet: false),
        DateTimeOffset.UtcNow);

    private static GacPlannerUnitDetails Unit(string id) => new(
        id,
        id,
        ThumbnailName: null,
        IsShip: false,
        GalacticPower: 50_000,
        RelicTier: 7,
        ZetaCount: 1,
        OmicronCount: 0);

    private sealed class FailingPlanner(CancellationTokenSource requestCancellation) : IGacPlannerService
    {
        private int createCalls;

        public List<Guid> CreatedPresetIds { get; } = [];

        public List<Guid> DeletedPresetIds { get; } = [];

        public List<bool> DeleteTokenWasCancelled { get; } = [];

        public Task<GacPlannerLookup> GetCurrentAsync(
            long allyCode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GacPlannerLookup> SaveCurrentAsync(
            long allyCode,
            SaveCurrentGacRoundPlan input,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GacTeamPresetDetails> CreatePresetAsync(
            long allyCode,
            SaveGacTeamPreset input,
            CancellationToken cancellationToken = default)
        {
            createCalls++;
            if (createCalls == 2)
            {
                requestCancellation.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }

            Guid id = Guid.NewGuid();
            CreatedPresetIds.Add(id);
            return Task.FromResult(Preset(id, input.Name, input.LeaderDefinitionId));
        }

        public Task<GacTeamPresetDetails?> UpdatePresetAsync(
            long allyCode,
            Guid id,
            SaveGacTeamPreset input,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeletePresetAsync(
            long allyCode,
            Guid id,
            CancellationToken cancellationToken = default)
        {
            DeletedPresetIds.Add(id);
            DeleteTokenWasCancelled.Add(cancellationToken.IsCancellationRequested);
            return Task.FromResult(true);
        }
    }
}
