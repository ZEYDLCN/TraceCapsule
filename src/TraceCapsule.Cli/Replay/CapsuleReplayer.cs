using System.Diagnostics;
using System.Text;
using TraceCapsule.Core;
using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Model;
using TraceCapsule.Core.Recording;

namespace TraceCapsule.Cli.Replay;

/// <summary>Phase 2's <c>tracecapsule replay</c>: re-issues a capsule's recorded inbound
/// request against a live target and turns the result into a brand-new capsule, so
/// <c>tracecapsule compare</c> has two real artifacts to diff. Fault-injection instructions
/// (Phase 6) ride along on <see cref="TraceCapsuleHeaders.FaultLatency"/> so the target's own
/// <c>ReplayHttpMessageHandler</c>/<c>QueueEmulator</c> can apply them to whatever it calls
/// downstream — this process has no way to reach into another process's dependencies
/// directly, so that's the boundary: this CLI replays the *inbound* call, the target app
/// (already wired with TraceCapsule) replays everything *inside* it.</summary>
public static class CapsuleReplayer
{
    private static readonly HashSet<string> SkippedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "content-length", "content-type", "connection", "transfer-encoding",
    };

    public static async Task<Capsule> ReplayAsync(Capsule original, Uri targetBaseUrl, FaultInjectionOptions faults, CancellationToken cancellationToken = default)
    {
        var request = original.Request ?? throw new InvalidOperationException(
            "This capsule has no recorded request (request.json is missing) — nothing to replay.");

        using var client = new HttpClient { BaseAddress = targetBaseUrl };
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Path + request.QueryString);
        if (!string.IsNullOrEmpty(request.Body))
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, request.ContentType ?? "application/json");
        }
        foreach (var (name, values) in request.Headers)
        {
            if (SkippedRequestHeaders.Contains(name)) continue;
            message.Headers.TryAddWithoutValidation(name, values);
        }
        var faultHeader = faults.ToHeaderValue();
        if (!string.IsNullOrEmpty(faultHeader)) message.Headers.TryAddWithoutValidation(TraceCapsuleHeaders.FaultLatency, faultHeader);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await client.SendAsync(message, cancellationToken);
            var bodyBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();

            return BuildResult(original, startedAt, stopwatch.Elapsed, response: new HttpResponseRecord
            {
                StatusCode = (int)response.StatusCode,
                Headers = response.Headers.Concat(response.Content.Headers).ToDictionary(h => h.Key, h => h.Value.ToArray()),
                Body = BodyCapture.FromBytes(bodyBytes, maxBytes: 64 * 1024),
                ContentType = response.Content.Headers.ContentType?.ToString(),
            }, exception: null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            return BuildResult(original, startedAt, stopwatch.Elapsed, response: null, exception: new ExceptionRecord
            {
                Type = ex.GetType().FullName ?? ex.GetType().Name,
                Message = ex.Message,
                Timestamp = DateTimeOffset.UtcNow,
            });
        }
    }

    private static Capsule BuildResult(Capsule original, DateTimeOffset startedAt, TimeSpan duration, HttpResponseRecord? response, ExceptionRecord? exception) => new()
    {
        Metadata = new CapsuleMetadata
        {
            TraceId = original.Metadata.TraceId,
            Timestamp = startedAt,
            Request = original.Metadata.Request,
            Services = original.Metadata.Services,
            Environment = original.Metadata.Environment,
        },
        Request = original.Request,
        Response = response,
        Exceptions = exception is null ? [] : [exception],
        Timing = new TimingRecord { StartedAt = startedAt, CompletedAt = startedAt + duration, TotalDurationMs = duration.TotalMilliseconds },
    };
}
