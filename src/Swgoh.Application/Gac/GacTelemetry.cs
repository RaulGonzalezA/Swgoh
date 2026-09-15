using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Swgoh.Application.Gac;

public interface IGacTelemetry
{
    ActivitySource ActivitySource { get; }

    Task<T> MeasurePhaseAsync<T>(
        string phase,
        Func<Task<T>> operation,
        string? format = null);

    T MeasurePhase<T>(
        string phase,
        Func<T> operation,
        string? format = null);

    void RecordPipeline(
        TimeSpan elapsed,
        CurrentGacOpponentStatus status,
        string? format,
        int warningCount);

    void RecordOpponentLookup(
        TimeSpan elapsed,
        CurrentGacOpponentStatus status,
        bool cacheHit,
        string source);
}

public sealed class GacTelemetry : IGacTelemetry, IDisposable
{
    public const string ActivitySourceName = "Swgoh.Gac";
    public const string MeterName = "Swgoh.Gac";

    private readonly Meter meter;
    private readonly Histogram<double> phaseDuration;
    private readonly Histogram<double> pipelineDuration;
    private readonly Histogram<double> opponentLookupDuration;
    private readonly Counter<long> opponentLookupRequests;
    private readonly Counter<long> degradedPipelines;

    public GacTelemetry()
    {
        ActivitySource = new ActivitySource(ActivitySourceName);
        meter = new Meter(MeterName);
        phaseDuration = meter.CreateHistogram<double>(
            "swgoh.gac.phase.duration",
            unit: "ms",
            description: "Duration of individual GAC pipeline phases.");
        pipelineDuration = meter.CreateHistogram<double>(
            "swgoh.gac.pipeline.duration",
            unit: "ms",
            description: "End-to-end duration of the GAC scouting pipeline.");
        opponentLookupDuration = meter.CreateHistogram<double>(
            "swgoh.gac.opponent_lookup.duration",
            unit: "ms",
            description: "Duration of GAC opponent lookup operations.");
        opponentLookupRequests = meter.CreateCounter<long>(
            "swgoh.gac.opponent_lookup.requests",
            unit: "{request}",
            description: "Number of GAC opponent lookup requests.");
        degradedPipelines = meter.CreateCounter<long>(
            "swgoh.gac.pipeline.degraded",
            unit: "{request}",
            description: "Number of GAC scouting pipelines that completed with degradation warnings.");
    }

    public ActivitySource ActivitySource { get; }

    public async Task<T> MeasurePhaseAsync<T>(
        string phase,
        Func<Task<T>> operation,
        string? format = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(operation);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using Activity? activity = StartPhaseActivity(phase, format);
        try
        {
            T result = await operation().ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            RecordPhase(phase, format, stopwatch.Elapsed);
        }
    }

    public T MeasurePhase<T>(
        string phase,
        Func<T> operation,
        string? format = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(operation);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using Activity? activity = StartPhaseActivity(phase, format);
        try
        {
            T result = operation();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            RecordPhase(phase, format, stopwatch.Elapsed);
        }
    }

    public void RecordPipeline(
        TimeSpan elapsed,
        CurrentGacOpponentStatus status,
        string? format,
        int warningCount)
    {
        TagList tags = default;
        tags.Add("gac.status", status.ToString());
        if (!string.IsNullOrWhiteSpace(format))
        {
            tags.Add("gac.format", format);
        }

        tags.Add("gac.degraded", warningCount > 0);
        pipelineDuration.Record(elapsed.TotalMilliseconds, tags);
        if (warningCount > 0)
        {
            degradedPipelines.Add(1, tags);
        }
    }

    public void RecordOpponentLookup(
        TimeSpan elapsed,
        CurrentGacOpponentStatus status,
        bool cacheHit,
        string source)
    {
        TagList tags = default;
        tags.Add("gac.status", status.ToString());
        tags.Add("gac.cache_hit", cacheHit);
        tags.Add("gac.lookup_source", source);
        opponentLookupDuration.Record(elapsed.TotalMilliseconds, tags);
        opponentLookupRequests.Add(1, tags);
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        meter.Dispose();
    }

    private Activity? StartPhaseActivity(string phase, string? format)
    {
        Activity? activity = ActivitySource.StartActivity($"gac.{phase}", ActivityKind.Internal);
        activity?.SetTag("gac.phase", phase);
        if (!string.IsNullOrWhiteSpace(format))
        {
            activity?.SetTag("gac.format", format);
        }

        return activity;
    }

    private void RecordPhase(string phase, string? format, TimeSpan elapsed)
    {
        TagList tags = default;
        tags.Add("gac.phase", phase);
        if (!string.IsNullOrWhiteSpace(format))
        {
            tags.Add("gac.format", format);
        }

        phaseDuration.Record(elapsed.TotalMilliseconds, tags);
    }
}
