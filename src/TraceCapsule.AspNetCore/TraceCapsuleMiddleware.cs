using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TraceCapsule.Core.Capsules;
using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Model;
using TraceCapsule.Core.Recording;
using TraceCapsule.Core.Redaction;

namespace TraceCapsule.AspNetCore;

/// <summary>The entry point of the whole recording pipeline (see the README's Architecture
/// diagram: "APPLICATION → TraceCapsule Middleware → HTTP Recorder / Trace Collector /
/// Event Recorder → Redaction Engine → Capsule Builder → .capsule File"). It buffers the
/// request body, swaps in a capturing response stream, opens an ambient
/// <see cref="CapsuleRecordingContext"/> so downstream recorders (HttpClient, RabbitMQ,
/// OpenTelemetry) have somewhere to report into, and — if the capture policy says this
/// execution is worth keeping — writes the resulting <see cref="Capsule"/> to
/// <see cref="TraceCapsuleOptions.OutputDirectory"/>.</summary>
public sealed class TraceCapsuleMiddleware(RequestDelegate next, IOptions<TraceCapsuleOptions> options, ILogger<TraceCapsuleMiddleware> logger)
{
    private readonly TraceCapsuleOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.RequireAttribute && context.GetEndpoint()?.Metadata.GetMetadata<TraceCapsuleAttribute>() is null)
        {
            await next(context);
            return;
        }

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var sessionId = ResolveSessionId(context);
        context.Response.Headers[_options.SessionHeaderName] = sessionId;

        context.Request.EnableBuffering();
        var requestBodyBytes = await ReadAllAsync(context.Request.Body);
        context.Request.Body.Position = 0;

        var originalResponseBody = context.Response.Body;
        await using var capturedResponseBody = new MemoryStream();
        context.Response.Body = capturedResponseBody;

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        Exception? caughtException = null;

        var faults = FaultInjectionOptions.ParseHeaderValue(context.Request.Headers[_options.FaultHeaderName]);
        using var faultScope = FaultInjectionContext.Begin(faults);
        using var scope = CapsuleRecordingContext.Begin(traceId, sessionId, out var recording);
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            caughtException = ex;
            recording.Exceptions.Add(new ExceptionRecord
            {
                Type = ex.GetType().FullName ?? ex.GetType().Name,
                Message = ex.Message,
                StackTrace = ex.StackTrace,
                Timestamp = DateTimeOffset.UtcNow,
            });
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var responseBodyBytes = capturedResponseBody.ToArray();
            capturedResponseBody.Position = 0;
            await capturedResponseBody.CopyToAsync(originalResponseBody);
            context.Response.Body = originalResponseBody;

            var isError = caughtException is not null || context.Response.StatusCode >= 500;
            var manualTrigger = context.Request.Headers.ContainsKey(_options.ManualCaptureHeaderName);
            if (_options.CapturePolicy.ShouldCapture(isError, stopwatch.Elapsed.TotalMilliseconds, manualTrigger))
            {
                var capsule = BuildCapsule(context, traceId, sessionId, requestBodyBytes, responseBodyBytes, startedAt, stopwatch.Elapsed, recording);
                await PersistAsync(capsule);
            }
        }
    }

    private string ResolveSessionId(HttpContext context) =>
        context.Request.Headers.TryGetValue(_options.SessionHeaderName, out var incoming) && !string.IsNullOrEmpty(incoming)
            ? incoming.ToString()
            : Guid.NewGuid().ToString("N");

    private Capsule BuildCapsule(
        HttpContext context, string traceId, string sessionId,
        byte[] requestBodyBytes, byte[] responseBodyBytes,
        DateTimeOffset startedAt, TimeSpan duration, CapsuleRecordingContext recording)
    {
        var redaction = _options.EnableRedaction ? new RedactionEngine(_options.RedactionPolicy) : null;
        var request = context.Request;
        var response = context.Response;

        var requestHeaders = ToHeaderDictionary(request.Headers);
        var responseHeaders = ToHeaderDictionary(response.Headers);
        var requestBody = BodyCapture.FromBytes(requestBodyBytes, _options.MaxCapturedBodyBytes);
        var responseBody = BodyCapture.FromBytes(responseBodyBytes, _options.MaxCapturedBodyBytes);

        if (redaction is not null)
        {
            requestHeaders = redaction.RedactHeaders(requestHeaders);
            responseHeaders = redaction.RedactHeaders(responseHeaders);
            requestBody = redaction.RedactJsonBody(requestBody);
            responseBody = redaction.RedactJsonBody(responseBody);
        }

        var exceptions = recording.Exceptions.ToList();
        var trace = recording.Spans.OrderBy(s => s.StartTime).ToList();

        // When the pipeline throws, the framework's own "turn this into a 500" logic runs
        // in the host, *outside* this middleware's frame — context.Response.StatusCode is
        // still whatever it was before the exception (typically the 200 default) by the
        // time our finally block runs. Reflect what the caller will actually see instead of
        // trusting a status code the response never got to set.
        var effectiveStatusCode = exceptions.Count > 0 && response.StatusCode < 500 ? 500 : response.StatusCode;

        // The request's own root Activity (ASP.NET Core's hosting instrumentation) only
        // *stops* after this middleware's own finally block has already run, so
        // CapsuleActivityListener can never observe it in time. Synthesize it here instead —
        // every capsule gets a root span for the request even with no listener registered.
        var rootActivity = Activity.Current;
        var rootSpanId = rootActivity?.SpanId.ToString() ?? traceId;
        if (!trace.Any(s => s.SpanId == rootSpanId))
        {
            trace.Insert(0, new SpanRecord
            {
                SpanId = rootSpanId,
                TraceId = traceId,
                Name = rootActivity?.DisplayName ?? $"{request.Method} {request.Path}",
                ServiceName = _options.AppVersion,
                StartTime = startedAt,
                EndTime = startedAt + duration,
                DurationMs = duration.TotalMilliseconds,
                Status = effectiveStatusCode >= 500 ? "Error" : "Ok",
            });
        }

        return new Capsule
        {
            Metadata = new CapsuleMetadata
            {
                TraceId = traceId,
                SessionId = sessionId,
                Timestamp = startedAt,
                Request = new RequestInfo { Method = request.Method, Path = request.Path },
                Services = [context.RequestServices.GetService<IHostEnvironmentProvider>()?.ServiceName ?? _options.AppVersion],
                Environment = new EnvironmentInfo { AppVersion = _options.AppVersion },
            },
            Request = new HttpRequestRecord
            {
                Method = request.Method, Path = request.Path, QueryString = request.QueryString.Value ?? "",
                Headers = requestHeaders, Body = requestBody, ContentType = request.ContentType,
            },
            Response = new HttpResponseRecord
            {
                StatusCode = effectiveStatusCode, Headers = responseHeaders, Body = responseBody, ContentType = response.ContentType,
            },
            Trace = trace,
            ExternalHttpCalls = recording.ExternalHttpCalls.OrderBy(c => c.Timestamp).ToList(),
            Events = recording.Events.OrderBy(e => e.Timestamp).ToList(),
            Exceptions = exceptions,
            Timing = new TimingRecord { StartedAt = startedAt, CompletedAt = startedAt + duration, TotalDurationMs = duration.TotalMilliseconds },
        };
    }

    private async Task PersistAsync(Capsule capsule)
    {
        try
        {
            // Distributed traces (Phase 5) give every participating service the *same*
            // trace id via standard W3C trace-context propagation (ASP.NET Core's hosting
            // instrumentation + HttpClient both do this automatically) — naming the file
            // after the trace id alone would let each service's write clobber the previous
            // one's. The random suffix keeps every service's partial capsule as its own file;
            // lookups (`tracecapsule export`) match on Metadata.TraceId, not the file name.
            var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
            var fileName = $"{capsule.Metadata.TraceId}-{uniqueSuffix}.capsule";
            var path = Path.Combine(_options.OutputDirectory, fileName);
            await CapsuleWriter.WriteAsync(capsule, path);
            logger.LogInformation("TraceCapsule wrote {Path}", path);
        }
        catch (Exception ex)
        {
            // Recording must never take down the request it's observing.
            logger.LogWarning(ex, "TraceCapsule failed to persist a capsule for trace {TraceId}", capsule.Metadata.TraceId);
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static Dictionary<string, string[]> ToHeaderDictionary(IHeaderDictionary headers) =>
        headers.ToDictionary(h => h.Key, h => h.Value.Select(v => v ?? "").ToArray());
}

/// <summary>Optional hook a host application can register to give the capsule's
/// <c>Metadata.Services</c> list a real name instead of the app version string.</summary>
public interface IHostEnvironmentProvider
{
    string ServiceName { get; }
}
