using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class CurrentGacScoutingCacheTests
{
    private static readonly DateTimeOffset PlayerVersion = DateTimeOffset.Parse("2026-09-15T04:00:00Z");
    private static readonly DateTimeOffset OpponentVersion = DateTimeOffset.Parse("2026-09-15T04:01:00Z");

    [Fact]
    public async Task GetOrCreateAsync_WithConcurrentRequests_ExecutesFactoryOnce()
    {
        var cache = new CurrentGacScoutingCache();
        CurrentGacScoutingCacheKey key = CreateKey(cache);
        CurrentGacScoutingResult expected = CreateResult();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int executions = 0;

        async Task<CurrentGacScoutingResult> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref executions);
            await release.Task.WaitAsync(cancellationToken);
            return expected;
        }

        Task<CurrentGacScoutingResult>[] requests =
        [
            .. Enumerable.Range(0, 8)
                .Select(_ => cache.GetOrCreateAsync(key, Factory))
        ];

        await Task.Yield();
        release.SetResult();
        CurrentGacScoutingResult[] results = await Task.WhenAll(requests);

        Assert.Equal(1, executions);
        Assert.All(results, result => Assert.Same(expected, result));
    }

    [Fact]
    public async Task GetOrCreateAsync_AfterSuccessfulResult_ReturnsCachedValue()
    {
        var cache = new CurrentGacScoutingCache();
        CurrentGacScoutingCacheKey key = CreateKey(cache);
        CurrentGacScoutingResult expected = CreateResult();
        int executions = 0;

        CurrentGacScoutingResult first = await cache.GetOrCreateAsync(key, _ =>
        {
            executions++;
            return Task.FromResult(expected);
        });
        CurrentGacScoutingResult second = await cache.GetOrCreateAsync(key, _ =>
        {
            executions++;
            return Task.FromResult(CreateResult());
        });

        Assert.Same(expected, first);
        Assert.Same(expected, second);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenCallerCancels_KeepsSharedOperationForOtherCallers()
    {
        var cache = new CurrentGacScoutingCache(TimeSpan.FromSeconds(5));
        CurrentGacScoutingCacheKey key = CreateKey(cache);
        CurrentGacScoutingResult expected = CreateResult();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int executions = 0;
        bool sharedTokenCancelled = false;

        async Task<CurrentGacScoutingResult> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref executions);
            started.TrySetResult();
            try
            {
                await release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                sharedTokenCancelled = true;
                throw;
            }

            return expected;
        }

        using var callerCancellation = new CancellationTokenSource();
        Task<CurrentGacScoutingResult> first = cache.GetOrCreateAsync(key, Factory, callerCancellation.Token);
        await started.Task;

        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        Task<CurrentGacScoutingResult> second = cache.GetOrCreateAsync(key, Factory);
        release.SetResult();
        CurrentGacScoutingResult result = await second;

        Assert.Same(expected, result);
        Assert.Equal(1, executions);
        Assert.False(sharedTokenCancelled);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenSharedOperationTimesOut_CancelsFactoryAndAllowsRetry()
    {
        var cache = new CurrentGacScoutingCache(TimeSpan.FromMilliseconds(100));
        CurrentGacScoutingCacheKey key = CreateKey(cache);
        CurrentGacScoutingResult expected = CreateResult();
        int executions = 0;

        async Task<CurrentGacScoutingResult> TimeoutFactory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref executions);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return expected;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.GetOrCreateAsync(key, TimeoutFactory));

        CurrentGacScoutingResult retried = await cache.GetOrCreateAsync(key, _ =>
        {
            Interlocked.Increment(ref executions);
            return Task.FromResult(expected);
        });

        Assert.Same(expected, retried);
        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task TryGet_WhenPlayerVersionChanges_DoesNotReturnPreviousScouting()
    {
        var cache = new CurrentGacScoutingCache();
        CurrentGacScoutingCacheKey originalKey = CreateKey(cache);
        CurrentGacScoutingResult expected = CreateResult();
        await cache.GetOrCreateAsync(originalKey, _ => Task.FromResult(expected));

        CurrentGacScoutingCacheKey refreshedKey = originalKey with
        {
            PlayerUpdatedAtUtc = originalKey.PlayerUpdatedAtUtc!.Value.AddMinutes(1)
        };

        Assert.False(cache.TryGet(refreshedKey, out _));
        Assert.True(cache.TryGet(originalKey, out CurrentGacScoutingResult? cached));
        Assert.Same(expected, cached);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenGenerationChanges_DoesNotRestoreStaleKey()
    {
        var cache = new CurrentGacScoutingCache();
        CurrentGacScoutingCacheKey originalKey = CreateKey(cache);
        CurrentGacScoutingResult expected = CreateResult();
        CurrentGacScoutingCacheKey refreshedKey = default;

        CurrentGacScoutingResult result = await cache.GetOrCreateAsync(originalKey, _ =>
        {
            cache.Invalidate(originalKey.AllyCode);
            refreshedKey = originalKey with
            {
                PlayerUpdatedAtUtc = originalKey.PlayerUpdatedAtUtc!.Value.AddMinutes(1),
                Generation = cache.GetGeneration(originalKey.AllyCode)
            };
            cache.Set(refreshedKey, expected);
            return Task.FromResult(expected);
        });

        Assert.Same(expected, result);
        Assert.False(cache.TryGet(originalKey, out _));
        Assert.True(cache.TryGet(refreshedKey, out CurrentGacScoutingResult? cached));
        Assert.Same(expected, cached);
    }

    [Fact]
    public void Invalidate_ChangesGeneration_AndMakesPreviousKeyUnreachable()
    {
        var cache = new CurrentGacScoutingCache();
        long generation = cache.GetGeneration(476825771);

        cache.Invalidate(476825771);

        Assert.True(cache.GetGeneration(476825771) > generation);
    }

    private static CurrentGacScoutingCacheKey CreateKey(CurrentGacScoutingCache cache) => new(
        AllyCode: 476825771,
        EventInstanceId: "event-instance",
        RoundNumber: 2,
        OpponentAllyCode: 123456789,
        Format: GacFormat.FiveVsFive,
        MaxRounds: 30,
        PlayerUpdatedAtUtc: PlayerVersion,
        OpponentUpdatedAtUtc: OpponentVersion,
        Generation: cache.GetGeneration(476825771));

    private static CurrentGacScoutingResult CreateResult()
    {
        var opponent = new CurrentGacOpponent(
            PlayerAllyCode: 476825771,
            OpponentAllyCode: 123456789,
            OpponentName: "Opponent",
            OpponentPlayerId: "opponent-player",
            League: GacLeague.Kyber,
            Format: GacFormat.FiveVsFive,
            EventId: "event",
            EventInstanceId: "event-instance",
            BracketId: "event-instance:KYBER:4178",
            RoundNumber: 2,
            FormatSource: "test",
            OpponentResolutionMethod: "test");
        return new CurrentGacScoutingResult(
            CurrentGacOpponentLookup.Found(opponent),
            Scouting: null,
            RosterScouting: null,
            BattlePlan: null);
    }
}
