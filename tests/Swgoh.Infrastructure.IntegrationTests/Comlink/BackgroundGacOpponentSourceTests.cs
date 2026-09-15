using System.Threading.Channels;

using Microsoft.Extensions.Logging.Abstractions;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Comlink;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Comlink;

public sealed class BackgroundGacOpponentSourceTests
{
    [Fact]
    public async Task PendingLookupSurvivesCallerCancellationAndReusesResult()
    {
        var provider = new DeferredSource();
        using var worker = new BackgroundGacOpponentSource(provider, new GacTelemetry(), NullLogger<BackgroundGacOpponentSource>.Instance);
        CancellationToken token = TestContext.Current.CancellationToken;
        await worker.StartAsync(token);
        try
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            Assert.Equal(CurrentGacOpponentStatus.Pending, (await worker.GetAsync(476825771, null, request.Token)).Status);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            request.Cancel();
            Assert.False(provider.WorkToken.IsCancellationRequested);
            Assert.Equal(CurrentGacOpponentStatus.Pending, (await worker.GetAsync(476825771, null, token)).Status);
            var expected = CurrentGacOpponentLookup.Unavailable(CurrentGacOpponentStatus.NoActiveEvent, "No event");
            provider.Result.SetResult(expected);
            Assert.Same(expected, await WaitForResultAsync(worker, token).WaitAsync(TimeSpan.FromSeconds(5), token));
            Assert.Equal(1, provider.Calls);
        }
        finally
        {
            await worker.StopAsync(token);
        }
    }

    [Fact]
    public async Task ProviderFailureCompletesWithUnavailable()
    {
        var provider = new DeferredSource();
        using var worker = new BackgroundGacOpponentSource(provider, new GacTelemetry(), NullLogger<BackgroundGacOpponentSource>.Instance);
        CancellationToken token = TestContext.Current.CancellationToken;
        await worker.StartAsync(token);
        try
        {
            await worker.GetAsync(476825771, null, token);
            provider.Result.SetException(new HttpRequestException("Connection aborted"));
            Assert.Equal(CurrentGacOpponentStatus.OpponentUnavailable,
                (await WaitForResultAsync(worker, token).WaitAsync(TimeSpan.FromSeconds(5), token)).Status);
        }
        finally
        {
            await worker.StopAsync(token);
        }
    }

    [Fact]
    public async Task Invalidate_DiscardsResultFromOlderGeneration()
    {
        var provider = new SequencedDeferredSource();
        using var worker = new BackgroundGacOpponentSource(provider, new GacTelemetry(), NullLogger<BackgroundGacOpponentSource>.Instance);
        CancellationToken token = TestContext.Current.CancellationToken;
        await worker.StartAsync(token);
        try
        {
            Assert.Equal(CurrentGacOpponentStatus.Pending, (await worker.GetAsync(476825771, null, token)).Status);
            SequencedDeferredSource.Invocation first = await provider.ReadNextAsync(token);

            worker.Invalidate(476825771);
            Assert.Equal(CurrentGacOpponentStatus.Pending, (await worker.GetAsync(476825771, null, token)).Status);

            var stale = CurrentGacOpponentLookup.Unavailable(CurrentGacOpponentStatus.PlayerNotJoined, "Stale result");
            first.Result.SetResult(stale);

            SequencedDeferredSource.Invocation second = await provider.ReadNextAsync(token);
            var fresh = CurrentGacOpponentLookup.Unavailable(CurrentGacOpponentStatus.NoActiveEvent, "Fresh result");
            second.Result.SetResult(fresh);

            CurrentGacOpponentLookup actual = await WaitForResultAsync(worker, token).WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.Same(fresh, actual);
            Assert.NotSame(stale, actual);
            Assert.Equal(2, provider.Calls);
        }
        finally
        {
            await worker.StopAsync(token);
        }
    }

    [Fact]
    public async Task DistinctPlayers_RunThreeLookupsConcurrently_WithoutHeadOfLineBlocking()
    {
        var provider = new ConcurrentDeferredSource();
        using var worker = new BackgroundGacOpponentSource(provider, new GacTelemetry(), NullLogger<BackgroundGacOpponentSource>.Instance);
        CancellationToken token = TestContext.Current.CancellationToken;
        await worker.StartAsync(token);
        try
        {
            long[] allyCodes = [476825771, 476825772, 476825773, 476825774];
            foreach (long allyCode in allyCodes)
            {
                Assert.Equal(CurrentGacOpponentStatus.Pending, (await worker.GetAsync(allyCode, null, token)).Status);
            }

            ConcurrentDeferredSource.Invocation[] firstWave =
            [
                await provider.ReadNextAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token),
                await provider.ReadNextAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token),
                await provider.ReadNextAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token)
            ];

            Assert.Equal(BackgroundGacOpponentSource.WorkerCount, provider.Calls);
            Assert.Equal(3, firstWave.Select(invocation => invocation.AllyCode).Distinct().Count());

            await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            Assert.Equal(BackgroundGacOpponentSource.WorkerCount, provider.Calls);

            firstWave[0].Result.SetResult(CurrentGacOpponentLookup.Unavailable(CurrentGacOpponentStatus.NoActiveEvent, "Done"));
            ConcurrentDeferredSource.Invocation fourth = await provider.ReadNextAsync(token)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.Equal(4, provider.Calls);
            Assert.Contains(fourth.AllyCode, allyCodes);
            Assert.DoesNotContain(fourth.AllyCode, firstWave.Select(invocation => invocation.AllyCode));

            foreach (ConcurrentDeferredSource.Invocation invocation in firstWave.Skip(1).Append(fourth))
            {
                invocation.Result.TrySetResult(CurrentGacOpponentLookup.Unavailable(CurrentGacOpponentStatus.NoActiveEvent, "Done"));
            }
        }
        finally
        {
            await worker.StopAsync(token);
        }
    }

    private static async Task<CurrentGacOpponentLookup> WaitForResultAsync(BackgroundGacOpponentSource worker, CancellationToken token)
    {
        while (true)
        {
            CurrentGacOpponentLookup result = await worker.GetAsync(476825771, null, token);
            if (result.Status != CurrentGacOpponentStatus.Pending)
            {
                return result;
            }

            await Task.Yield();
        }
    }

    private sealed class DeferredSource : ILiveGacOpponentSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CurrentGacOpponentLookup> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken WorkToken { get; private set; }
        public int Calls { get; private set; }

        public Task<CurrentGacOpponentLookup> GetAsync(long allyCode, GacFormat? formatOverride, CancellationToken cancellationToken = default)
        {
            Calls++;
            WorkToken = cancellationToken;
            Started.SetResult();
            return Result.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class SequencedDeferredSource : ILiveGacOpponentSource
    {
        private readonly Channel<Invocation> invocations = Channel.CreateUnbounded<Invocation>();
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task<CurrentGacOpponentLookup> GetAsync(
            long allyCode,
            GacFormat? formatOverride,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            var invocation = new Invocation(new TaskCompletionSource<CurrentGacOpponentLookup>(TaskCreationOptions.RunContinuationsAsynchronously));
            invocations.Writer.TryWrite(invocation);
            return invocation.Result.Task.WaitAsync(cancellationToken);
        }

        public ValueTask<Invocation> ReadNextAsync(CancellationToken cancellationToken) =>
            invocations.Reader.ReadAsync(cancellationToken);

        public sealed record Invocation(TaskCompletionSource<CurrentGacOpponentLookup> Result);
    }

    private sealed class ConcurrentDeferredSource : ILiveGacOpponentSource
    {
        private readonly Channel<Invocation> invocations = Channel.CreateUnbounded<Invocation>();
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task<CurrentGacOpponentLookup> GetAsync(
            long allyCode,
            GacFormat? formatOverride,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            var invocation = new Invocation(
                allyCode,
                new TaskCompletionSource<CurrentGacOpponentLookup>(TaskCreationOptions.RunContinuationsAsynchronously));
            invocations.Writer.TryWrite(invocation);
            return invocation.Result.Task.WaitAsync(cancellationToken);
        }

        public ValueTask<Invocation> ReadNextAsync(CancellationToken cancellationToken) =>
            invocations.Reader.ReadAsync(cancellationToken);

        public sealed record Invocation(long AllyCode, TaskCompletionSource<CurrentGacOpponentLookup> Result);
    }
}
