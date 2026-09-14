using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Swgoh.Application.Gac;

public static class GacTelemetry
{
    public const string ActivitySourceName = "Swgoh.Gac";
    public const string MeterName = "Swgoh.Gac";

    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> PhaseDuration = Meter.CreateHistogram<double>(
        "swgoh.gac.phase.duration",
        unit: "ms",
        description: "Duration of individual GAC pipeline phases.");
    private static readonly Histogram<double> PipelineDuration = Meter.CreateHistogram<double>(
        "swgoh.gac.pipeline.duration",
        unit: "ms",
        description: "End-to-end duration of the GAC scouting pipeline.");
    private static readonly Histogram<double> OpponentLookupDuration = Meter.CreateHistogram<double>(
        "swgoh.gac.opponent_lookup.duration",
        unit: "ms",
        description: "Duration of GAC opponent lookup operations.");
    private static readonly Counter<long> OpponentLookupRequests = Meter.CreateCounter<long>(
        "swgoh.gac.opponent_lookup.requests",
        unit: "{request}",
        description: "Number of GAC opponent lookup requests.");
    private static readonly Counter<long> DegradedPipelines = Meter.CreateCounter<long>(
        "swgoh.gac.pipeline.degraded",
        unit: "{request}",
        description: "Number of GAC scouting pipelines that completed with degradation warnings.");

    public static async Task<T> MeasurePhaseAsync<T>(
        string phase,
        Func<Task<T>> operation,
        string? format = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(operation);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using Activity? activity = ActivitySource.StartActivity($"gac.{phase}", ActivityKind.Internal);
        activity?.SetTag("gac.phase", phase);
        if (!string.IsNullOrWhiteSpace(format))
        {
            activity?.SetTag("gac.format", format);
        }

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
            TagList tags = default;
            tags.Add("gac.phase", phase);
            if (!string.IsNullOrWhiteSpace(format))
            {
                tags.Add("gac.format", format);
            }

            PhaseDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
        }
    }

    public static void RecordPipeline(
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
        PipelineDuration.Record(elapsed.TotalMilliseconds, tags);
        if (warningCount > 0)
        {
            DegradedPipelines.Add(1, tags);
        }
    }

    public static void RecordOpponentLookup(
        TimeSpan elapsed,
        CurrentGacOpponentStatus status,
        bool cacheHit,
        string source)
    {
        TagList tags = default;
        tags.Add("gac.status", status.ToString());
        tags.Add("gac.cache_hit", cacheHit);
        tags.Add("gac.lookup_source", source);
        OpponentLookupDuration.Record(elapsed.TotalMilliseconds, tags);
        OpponentLookupRequests.Add(1, tags);
    }
}
