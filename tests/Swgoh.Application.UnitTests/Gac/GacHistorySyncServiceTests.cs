using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacHistorySyncServiceTests
{
    [Fact]
    public async Task SyncAsync_ImportsRoundsFromEnabledProvidersAndReportsFailures()
    {
        GacHistoryRoundInput round = new(
            Season: 83,
            EventNumber: 1,
            RoundNumber: 1,
            Format: GacFormat.ThreeVsThree,
            League: GacLeague.Kyber,
            StartedAtUtc: DateTimeOffset.Parse("2026-09-09T18:00:00Z"),
            FullClear: true,
            Source: "provider",
            Defenses: [],
            OffenseBattles: []);
        IGacHistoryProvider[] providers =
        [
            new FakeProvider("good", enabled: true, [round]),
            new ThrowingProvider("bad"),
            new FakeProvider("disabled", enabled: false, [round])
        ];
        var history = new RecordingHistoryService();
        var service = new GacHistorySyncService(providers, history);

        GacHistorySyncResult result = await service.SyncAsync(
            123456789,
            GacFormat.ThreeVsThree,
            30,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ProvidersAttempted);
        Assert.Equal(1, result.ProvidersSucceeded);
        Assert.Equal(1, result.RoundsReceived);
        Assert.Equal(1, result.RoundsImported);
        Assert.Equal(["good"], result.Sources);
        Assert.Single(result.Warnings);
        Assert.Contains("bad", result.Warnings.Single(), StringComparison.Ordinal);
        Assert.Equal(1, history.ImportCallCount);
    }

    [Fact]
    public async Task SyncAsync_WithNoEnabledProvider_ReportsConfigurationWarning()
    {
        var history = new RecordingHistoryService();
        var service = new GacHistorySyncService(
            [new FakeProvider("disabled", enabled: false, [])],
            history);

        GacHistorySyncResult result = await service.SyncAsync(
            123456789,
            GacFormat.FiveVsFive,
            30,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ProvidersAttempted);
        Assert.Equal(0, result.RoundsImported);
        Assert.False(result.HasConfiguredProvider);
        Assert.Single(result.Warnings);
        Assert.Contains("proveedor histórico", result.Warnings.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, history.ImportCallCount);
    }

    private sealed class FakeProvider(
        string name,
        bool enabled,
        IReadOnlyCollection<GacHistoryRoundInput> rounds) : IGacHistoryProvider
    {
        public string Name => name;
        public bool IsEnabled => enabled;

        public Task<IReadOnlyCollection<GacHistoryRoundInput>> GetAsync(
            long allyCode,
            GacFormat format,
            int maxRounds,
            CancellationToken cancellationToken = default) => Task.FromResult(rounds);
    }

    private sealed class ThrowingProvider(string name) : IGacHistoryProvider
    {
        public string Name => name;
        public bool IsEnabled => true;

        public Task<IReadOnlyCollection<GacHistoryRoundInput>> GetAsync(
            long allyCode,
            GacFormat format,
            int maxRounds,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("provider unavailable");
    }

    private sealed class RecordingHistoryService : IGacHistoryService
    {
        public int ImportCallCount { get; private set; }

        public Task<GacHistoryImportResult> ImportAsync(
            long allyCode,
            IReadOnlyCollection<GacHistoryRoundInput> rounds,
            CancellationToken cancellationToken = default)
        {
            ImportCallCount++;
            return Task.FromResult(new GacHistoryImportResult(rounds.Count));
        }

        public Task<IReadOnlyCollection<GacHistoricalRound>> GetAsync(
            long allyCode,
            GacHistoryQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacHistoricalRound>>([]);
    }
}
