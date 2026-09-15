using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacGeneratedDefenseCleanupServiceTests
{
    private const long AllyCode = 123456789;
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeleteAsync_RemovesOnlyLifecycleTrackedSmartDefenseAndReleasesCurrentDefense()
    {
        GacTeamPreset autoDefense = Preset("Auto · Jedi · defensa", GacPlannerTeamUse.Defense, "AUTO");
        GacTeamPreset manualDefense = Preset("Mi defensa", GacPlannerTeamUse.Defense, "MANUAL");
        GacTeamPreset autoAttack = Preset("Auto ATK · Jedi", GacPlannerTeamUse.Offense, "ATTACK");
        var presetRepository = new FakePresetRepository([autoDefense, manualDefense, autoAttack]);
        var strategyRepository = new FakeStrategyRepository(new GacDefenseStrategyProfile(
            AllyCode,
            GacFormat.FiveVsFive,
            [
                new GacDefenseTemplateSlot(1, "Sur frontal", autoDefense.Id),
                new GacDefenseTemplateSlot(2, "Norte frontal", manualDefense.Id)
            ],
            [autoDefense.Id],
            Now));
        GacPlannerState state = State(
            [Details(autoDefense), Details(manualDefense), Details(autoAttack)],
            [
                new GacOwnDefenseAssignmentDetails(Guid.NewGuid(), "Sur frontal", Details(autoDefense)),
                new GacOwnDefenseAssignmentDetails(Guid.NewGuid(), "Norte frontal", Details(manualDefense))
            ]);
        var plannerService = new FakePlannerService(state);
        var lifecycleService = new FakeLifecycleService([autoDefense.Id]);
        var service = new GacGeneratedDefenseCleanupService(
            strategyRepository,
            presetRepository,
            plannerService,
            lifecycleService,
            new FixedClock(Now.AddMinutes(1)));

        GacGeneratedDefenseCleanupResult result = await service.DeleteAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.DeletedPresets);
        Assert.Equal(1, result.RemovedDefenseAssignments);
        Assert.Equal([autoDefense.Name], result.DeletedTeamNames);
        Assert.DoesNotContain(autoDefense.Id, presetRepository.Presets.Keys);
        Assert.Contains(manualDefense.Id, presetRepository.Presets.Keys);
        Assert.Contains(autoAttack.Id, presetRepository.Presets.Keys);
        Assert.DoesNotContain(autoDefense.Id, lifecycleService.GeneratedIds);

        SaveCurrentGacRoundPlan savedPlan = Assert.IsType<SaveCurrentGacRoundPlan>(plannerService.SavedPlan);
        SaveGacOwnDefenseAssignment remaining = Assert.Single(savedPlan.OwnDefenses);
        Assert.Equal(manualDefense.Id, remaining.TeamPresetId);

        GacDefenseStrategyProfile stored = Assert.IsType<GacDefenseStrategyProfile>(strategyRepository.Stored);
        Assert.Null(stored.Slots.OrderBy(slot => slot.Position).First().PinnedTeamPresetId);
        Assert.Equal(manualDefense.Id, stored.Slots.OrderBy(slot => slot.Position).Last().PinnedTeamPresetId);
        Assert.DoesNotContain(autoDefense.Id, stored.ReservedAttackPresetIds);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotDeleteAutoNamedTeamWhenLifecycleDoesNotOwnIt()
    {
        GacTeamPreset manual = Preset("Auto · nombre elegido por usuario", GacPlannerTeamUse.Defense, "MANUAL");
        var presetRepository = new FakePresetRepository([manual]);
        var strategyRepository = new FakeStrategyRepository(null);
        var lifecycleService = new FakeLifecycleService([]);
        var service = new GacGeneratedDefenseCleanupService(
            strategyRepository,
            presetRepository,
            new FakePlannerService(State([Details(manual)], [])),
            lifecycleService,
            new FixedClock(Now));

        GacGeneratedDefenseCleanupResult result = await service.DeleteAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.DeletedPresets);
        Assert.Contains(manual.Id, presetRepository.Presets.Keys);
    }

    private static GacTeamPreset Preset(string name, GacPlannerTeamUse use, string seed) =>
        GacTeamPreset.Create(
            Guid.NewGuid(),
            AllyCode,
            name,
            GacFormat.FiveVsFive,
            use,
            GacPlannerSquad.Create(
                GacFormat.FiveVsFive,
                $"{seed}-L",
                [$"{seed}-1", $"{seed}-2", $"{seed}-3", $"{seed}-4"],
                isFleet: false),
            Now);

    private static GacTeamPresetDetails Details(GacTeamPreset preset) => new(
        preset.Id,
        preset.AllyCode,
        preset.Name,
        preset.Format,
        preset.Use,
        new GacPlannerSquadDetails(
            Unit(preset.Squad.LeaderDefinitionId),
            [.. preset.Squad.MemberDefinitionIds.Select(Unit)],
            preset.Squad.IsFleet),
        preset.UpdatedAtUtc);

    private static GacPlannerUnitDetails Unit(string id) => new(
        id,
        id,
        null,
        false,
        20_000,
        7,
        1,
        0);

    private static GacPlannerState State(
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        IReadOnlyCollection<GacOwnDefenseAssignmentDetails> ownDefenses)
    {
        var opponent = new CurrentGacOpponent(
            AllyCode,
            987654321,
            "Opponent",
            "opponent-id",
            GacLeague.Kyber,
            GacFormat.FiveVsFive,
            "event",
            "event-instance",
            "bracket",
            1,
            "test",
            "test");
        var plan = new GacRoundPlanDetails(
            GacRoundPlan.BuildId(AllyCode, "event-instance", 1),
            AllyCode,
            987654321,
            "Opponent",
            "event",
            "event-instance",
            1,
            GacFormat.FiveVsFive,
            GacLeague.Kyber,
            ownDefenses,
            [],
            [],
            [],
            [],
            Now,
            Version: 3);
        return new GacPlannerState(opponent, presets, plan);
    }

    private sealed class FakePresetRepository(IEnumerable<GacTeamPreset> presets) : IGacTeamPresetRepository
    {
        public Dictionary<Guid, GacTeamPreset> Presets { get; } = presets.ToDictionary(item => item.Id);

        public Task<GacTeamPreset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Presets.GetValueOrDefault(id));

        public Task<IReadOnlyCollection<GacTeamPreset>> GetAsync(
            long allyCode,
            GacFormat? format,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacTeamPreset>>(
                [.. Presets.Values.Where(item => item.AllyCode == allyCode && (format is null || item.Format == format))]);

        public Task UpsertAsync(GacTeamPreset preset, CancellationToken cancellationToken = default)
        {
            Presets[preset.Id] = preset;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Presets.Remove(id));
    }

    private sealed class FakeStrategyRepository(GacDefenseStrategyProfile? stored) : IGacDefenseStrategyRepository
    {
        public GacDefenseStrategyProfile? Stored { get; private set; } = stored;

        public Task<GacDefenseStrategyProfile?> FindAsync(
            long allyCode,
            GacFormat format,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored?.AllyCode == allyCode && Stored.Format == format ? Stored : null);

        public Task UpsertAsync(
            GacDefenseStrategyProfile profile,
            CancellationToken cancellationToken = default)
        {
            Stored = profile;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLifecycleService(IEnumerable<Guid> generatedIds) : IGacGeneratedTeamLifecycleService
    {
        public HashSet<Guid> GeneratedIds { get; } = generatedIds.ToHashSet();

        public Task<IReadOnlySet<Guid>> GetGeneratedPresetIdsAsync(
            long allyCode,
            GacFormat format,
            GacGeneratedTeamOrigin? origin = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(GeneratedIds);

        public Task RegisterAsync(
            long allyCode,
            GacFormat format,
            GacGeneratedTeamOrigin origin,
            string generationId,
            string roundPlanId,
            IReadOnlyCollection<Guid> presetIds,
            CancellationToken cancellationToken = default)
        {
            GeneratedIds.UnionWith(presetIds);
            return Task.CompletedTask;
        }

        public Task<int> PruneUnreferencedAsync(
            long allyCode,
            GacPlannerState state,
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task ForgetAsync(
            IReadOnlyCollection<Guid> presetIds,
            CancellationToken cancellationToken = default)
        {
            GeneratedIds.ExceptWith(presetIds);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlannerService(GacPlannerState state) : IGacPlannerService
    {
        public SaveCurrentGacRoundPlan? SavedPlan { get; private set; }

        public Task<GacPlannerLookup> GetCurrentAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GacPlannerLookup(CurrentGacOpponentStatus.Found, null, state));

        public Task<GacPlannerLookup> SaveCurrentAsync(
            long allyCode,
            SaveCurrentGacRoundPlan input,
            CancellationToken cancellationToken = default)
        {
            SavedPlan = input;
            return Task.FromResult(new GacPlannerLookup(CurrentGacOpponentStatus.Found, null, state));
        }

        public Task<GacTeamPresetDetails> CreatePresetAsync(
            long allyCode,
            SaveGacTeamPreset input,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GacTeamPresetDetails?> UpdatePresetAsync(
            long allyCode,
            Guid id,
            SaveGacTeamPreset input,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeletePresetAsync(
            long allyCode,
            Guid id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
