using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class CurrentGacScoutingCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_WithConcurrentRequests_ExecutesFactoryOnce()
    {
        var cache = new CurrentGacScoutingCache();
        CurrentGacScoutingCacheKey key = CreateKey(cache);
        CurrentGacScoutingResult expected = CreateResult();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int executions = 0;

        async Task<CurrentGacScoutingResult> Factory()
        {
            Interlocked.Increment(ref executions);
            await release.Task;
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

        CurrentGacScoutingResult first = await cache.GetOrCreateAsync(key, () =>
        {
            executions++;
            return Task.FromResult(expected);
        });
        CurrentGacScoutingResult second = await cache.GetOrCreateAsync(key, () =>
        {
            executions++;
            return Task.FromResult(CreateResult());
        });

        Assert.Same(expected, first);
        Assert.Same(expected, second);
        Assert.Equal(1, executions);
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
