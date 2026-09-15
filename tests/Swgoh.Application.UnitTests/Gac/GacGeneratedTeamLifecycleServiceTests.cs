using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacGeneratedTeamLifecycleServiceTests
{
    private const long AllyCode = 123456789;
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegisterAsync_TracksGenerationOriginAndRound()
    {
        GacTeamPreset generated = Preset("Counter generado", GacPlannerTeamUse.Offense, "ATK");
        var lifecycleRepository = new FakeLifecycleRepository();
        var service = CreateService(
            lifecycleRepository,
            new FakePresetRepository([generated]),
            new FakeStrategyRepository(null));

        await service.RegisterAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            GacGeneratedTeamOrigin.CounterEngine,
            "generation-1",
            "round-1",
            [generated.Id],
            TestContext.Current.CancellationToken);

        GacGeneratedTeamLifecycleEntry entry = Assert.Single(lifecycleRepository.Entries.Values);
        Assert.Equal(generated.Id, entry.PresetId);
        Assert.Equal(GacGeneratedTeamOrigin.CounterEngine, entry.Origin);
        Assert.Equal("generation-1", entry.GenerationId);
        Assert.Equal("round-1", entry.RoundPlanId);
        Assert.Equal(Now, entry.CreatedAtUtc);
    }

    [Fact]
    public async Task GetGeneratedPresetIdsAsync_MigratesLegacyNamesByOrigin()
    {
        GacTeamPreset smartDefense = Preset("Auto · Jedi · defensa", GacPlannerTeamUse.Defense, "DEF");
        GacTeamPreset counter = Preset("Auto ATK · Sith", GacPlannerTeamUse.Offense, "ATK");
        GacTeamPreset manual = Preset("Mi equipo", GacPlannerTeamUse.Defense, "MAN");
        var service = CreateService(
            new FakeLifecycleRepository(),
            new FakePresetRepository([smartDefense, counter, manual]),
            new FakeStrategyRepository(null));

        IReadOnlySet<Guid> defenseIds = await service.GetGeneratedPresetIdsAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            GacGeneratedTeamOrigin.SmartDefense,
            TestContext.Current.CancellationToken);
        IReadOnlySet<Guid> counterIds = await service.GetGeneratedPresetIdsAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            GacGeneratedTeamOrigin.CounterEngine,
            TestContext.Current.CancellationToken);

        Assert.Equal([smartDefense.Id], defenseIds);
        Assert.Equal([counter.Id], counterIds);
        Assert.DoesNotContain(manual.Id, defenseIds);
        Assert.DoesNotContain(manual.Id, counterIds);
    }

    [Fact]
    public async Task PruneUnreferencedAsync_DeletesOnlyStaleGeneratedTeamsAndCleansStrategy()
    {
        GacTeamPreset active = Preset("Generated active", GacPlannerTeamUse.Defense, "ACTIVE");
        GacTeamPreset stale = Preset("Generated stale", GacPlannerTeamUse.Defense, "STALE");
        GacTeamPreset manual = Preset("Manual", GacPlannerTeamUse.Defense, "MANUAL");
        var lifecycleRepository = new FakeLifecycleRepository(
        [
            Entry(active.Id, GacGeneratedTeamOrigin.SmartDefense, "g1"),
            Entry(stale.Id, GacGeneratedTeamOrigin.SmartDefense, "g0")
        ]);
        var presetRepository = new FakePresetRepository([active, stale, manual]);
        var strategyRepository = new FakeStrategyRepository(new GacDefenseStrategyProfile(
            AllyCode,
            GacFormat.FiveVsFive,
            [
                new GacDefenseTemplateSlot(1, "Sur frontal", stale.Id),
                new GacDefenseTemplateSlot(2, "Norte frontal", manual.Id)
            ],
            [stale.Id],
            Now));
        var service = CreateService(lifecycleRepository, presetRepository, strategyRepository);
        GacPlannerState state = State(
            [Details(active), Details(stale), Details(manual)],
            [new GacOwnDefenseAssignmentDetails(Guid.NewGuid(), "Sur frontal", Details(active))]);

        int deleted = await service.PruneUnreferencedAsync(
            AllyCode,
            state,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, deleted);
        Assert.Contains(active.Id, presetRepository.Presets.Keys);
        Assert.DoesNotContain(stale.Id, presetRepository.Presets.Keys);
        Assert.Contains(manual.Id, presetRepository.Presets.Keys);
        Assert.Contains(active.Id, lifecycleRepository.Entries.Keys);
        Assert.DoesNotContain(stale.Id, lifecycleRepository.Entries.Keys);

        GacDefenseStrategyProfile stored = Assert.IsType<GacDefenseStrategyProfile>(strategyRepository.Stored);
        Assert.Null(stored.Slots.OrderBy(slot => slot.Position).First().PinnedTeamPresetId);
        Assert.Equal(manual.Id, stored.Slots.OrderBy(slot => slot.Position).Last().PinnedTeamPresetId);
        Assert.DoesNotContain(stale.Id, stored.ReservedAttackPresetIds);
    }

    private static GacGeneratedTeamLifecycleService CreateService(
        FakeLifecycleRepository lifecycleRepository,
        FakePresetRepository presetRepository,
        FakeStrategyRepository strategyRepository) => new(
        lifecycleRepository,
        presetRepository,
        strategyRepository,
        new FixedClock(Now));

    private static GacGeneratedTeamLifecycleEntry Entry(
        Guid presetId,
        GacGeneratedTeamOrigin origin,
        string generationId) => new(
        presetId,
        AllyCode,
        GacFormat.FiveVsFive,
        origin,
        generationId,
        "round-1",
        Now);

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
            Version: 1);
        return new GacPlannerState(opponent, presets, plan);
    }

    private sealed class FakeLifecycleRepository(
        IEnumerable<GacGeneratedTeamLifecycleEntry>? entries = null) : IGacGeneratedTeamLifecycleRepository
    {
        public Dictionary<Guid, GacGeneratedTeamLifecycleEntry> Entries { get; } =
            (entries ?? []).ToDictionary(entry => entry.PresetId);

        public Task<IReadOnlyCollection<GacGeneratedTeamLifecycleEntry>> GetAsync(
            long allyCode,
            GacFormat? format,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacGeneratedTeamLifecycleEntry>>(
                [.. Entries.Values.Where(entry =>
                    entry.AllyCode == allyCode && (format is null || entry.Format == format))]);

        public Task UpsertAsync(
            GacGeneratedTeamLifecycleEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries[entry.PresetId] = entry;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid presetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.Remove(presetId));
    }

    private sealed class FakePresetRepository(IEnumerable<GacTeamPreset> presets) : IGacTeamPresetRepository
    {
        public Dictionary<Guid, GacTeamPreset> Presets { get; } = presets.ToDictionary(preset => preset.Id);

        public Task<GacTeamPreset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Presets.GetValueOrDefault(id));

        public Task<IReadOnlyCollection<GacTeamPreset>> GetAsync(
            long allyCode,
            GacFormat? format,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacTeamPreset>>(
                [.. Presets.Values.Where(preset =>
                    preset.AllyCode == allyCode && (format is null || preset.Format == format))]);

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

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
