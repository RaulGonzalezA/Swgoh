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
        using var worker = new BackgroundGacOpponentSource(provider, NullLogger<BackgroundGacOpponentSource>.Instance);
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
        using var worker = new BackgroundGacOpponentSource(provider, NullLogger<BackgroundGacOpponentSource>.Instance);
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

    private sealed class DeferredSource : ICurrentGacOpponentSource
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
}
