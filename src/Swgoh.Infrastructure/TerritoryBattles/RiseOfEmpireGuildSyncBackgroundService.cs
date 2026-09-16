using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Swgoh.Application.Abstractions;
using Swgoh.Application.TerritoryBattles;

namespace Swgoh.Infrastructure.TerritoryBattles;

internal sealed class RiseOfEmpireGuildSyncBackgroundService(
    RiseOfEmpireGuildSyncQueue queue,
    IRiseOfEmpireGuildSyncJobRepository repository,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<RiseOfEmpireGuildSyncBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await queue.ResumePendingAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            await foreach (string jobId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessAsync(jobId, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown. Recoverable jobs remain persisted for the next start.
        }
    }

    private async Task ProcessAsync(string jobId, CancellationToken stoppingToken)
    {
        RiseOfEmpireGuildSyncJob? job = await repository.FindByIdAsync(jobId, stoppingToken).ConfigureAwait(false);
        if (job is null || job.IsTerminal)
        {
            return;
        }

        using var progress = new PersistedProgressSink(job.Id, repository, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(JobTimeout);
        try
        {
            await progress.ReportAsync(
                new RiseOfEmpireGuildSyncProgress(RiseOfEmpireGuildSyncStatus.DiscoveringMembers, 0, 0, 0),
                deadline.Token).ConfigureAwait(false);

            using IServiceScope scope = scopeFactory.CreateScope();
            IRiseOfEmpireGuildService? guildService = scope.ServiceProvider.GetService<IRiseOfEmpireGuildService>();
            if (guildService is not IRiseOfEmpireGuildSyncRunner runner)
            {
                throw new InvalidOperationException("Rise of the Empire guild sync runner is not registered.");
            }

            RiseOfEmpireGuildAnalysis? analysis = await runner
                .SyncAsync(job.AllyCode, progress, deadline.Token)
                .ConfigureAwait(false);
            if (analysis is null)
            {
                await MarkFailedAsync(job.Id, "No se encontró el jugador usado para iniciar la sincronización.", stoppingToken)
                    .ConfigureAwait(false);
                return;
            }

            RiseOfEmpireGuildSyncJob? latest = await repository.FindByIdAsync(job.Id, stoppingToken).ConfigureAwait(false);
            if (latest is null)
            {
                return;
            }

            DateTimeOffset now = clock.UtcNow;
            int totalMembers = latest.TotalMembers > 0 ? latest.TotalMembers : analysis.DetectedMembers;
            var completed = latest with
            {
                Status = RiseOfEmpireGuildSyncStatus.Completed,
                TotalMembers = totalMembers,
                CompletedMembers = totalMembers,
                CurrentMember = null,
                GuildId = analysis.GuildId,
                GuildName = analysis.GuildName,
                UpdatedAtUtc = now,
                StartedAtUtc = latest.StartedAtUtc ?? now,
                CompletedAtUtc = now,
                Error = null
            };
            await repository.UpsertAsync(completed, stoppingToken).ConfigureAwait(false);
            logger.LogInformation(
                "RotE guild sync {JobId} completed for {AllyCode}: {CompletedMembers}/{TotalMembers}, {FailedMembers} failed",
                job.Id,
                job.AllyCode,
                completed.CompletedMembers,
                completed.TotalMembers,
                completed.FailedMembers);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Keep the job recoverable on application shutdown.
        }
        catch (OperationCanceledException)
        {
            await MarkFailedAsync(job.Id, "La sincronización del gremio superó el tiempo máximo de 20 minutos.", stoppingToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "RotE guild sync {JobId} failed for {AllyCode}", job.Id, job.AllyCode);
            await MarkFailedAsync(job.Id, "No se pudo completar la sincronización del gremio.", stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task MarkFailedAsync(string jobId, string error, CancellationToken cancellationToken)
    {
        RiseOfEmpireGuildSyncJob? current = await repository.FindByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return;
        }

        DateTimeOffset now = clock.UtcNow;
        await repository.UpsertAsync(
            current with
            {
                Status = RiseOfEmpireGuildSyncStatus.Failed,
                CurrentMember = null,
                UpdatedAtUtc = now,
                StartedAtUtc = current.StartedAtUtc ?? now,
                CompletedAtUtc = now,
                Error = error
            },
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class PersistedProgressSink(
        string jobId,
        IRiseOfEmpireGuildSyncJobRepository repository,
        IClock clock) : IRiseOfEmpireGuildSyncProgressSink, IDisposable
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        public async ValueTask ReportAsync(
            RiseOfEmpireGuildSyncProgress progress,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RiseOfEmpireGuildSyncJob? current = await repository
                    .FindByIdAsync(jobId, cancellationToken)
                    .ConfigureAwait(false);
                if (current is null || current.IsTerminal)
                {
                    return;
                }

                DateTimeOffset now = clock.UtcNow;
                var updated = current with
                {
                    Status = progress.Status > current.Status ? progress.Status : current.Status,
                    TotalMembers = Math.Max(current.TotalMembers, progress.TotalMembers),
                    CompletedMembers = Math.Max(current.CompletedMembers, progress.CompletedMembers),
                    FailedMembers = Math.Max(current.FailedMembers, progress.FailedMembers),
                    CurrentMember = progress.CurrentMember,
                    GuildId = progress.GuildId ?? current.GuildId,
                    GuildName = progress.GuildName ?? current.GuildName,
                    UpdatedAtUtc = now,
                    StartedAtUtc = current.StartedAtUtc ?? now
                };
                await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public void Dispose() => gate.Dispose();
    }
}
