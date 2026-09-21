using System.Diagnostics;
using TraceCapsule.Core;
using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Model;
using TraceCapsule.Core.Recording;

namespace TraceCapsule.Http;

/// <summary>Phase 3: wraps an outbound <c>HttpClient</c> pipeline and records every call
/// into the ambient <see cref="CapsuleRecordingContext"/> as an
/// <see cref="ExternalHttpCallRecord"/>, so it can be replayed later without hitting the
/// real dependency (<see cref="ReplayHttpMessageHandler"/>). It also forwards the current
/// session id and any active fault-injection instructions onto the outbound request — this
/// is what makes Phase 5 (distributed capsules) and Phase 6 (chaos replay) actually
/// propagate across a real service-to-service call, not just a single CLI-initiated hop.
/// No-ops (recording-wise) if there's no capsule currently being recorded (e.g. a background
/// job outside any request) — headers are still forwarded if a fault scope is active.
/// <code>
/// services.AddHttpClient("fraud-api")
///     .AddTraceCapsuleRecording("fraud-api");
/// </code>
/// </summary>
public sealed class RecordingHttpMessageHandler(string dependencyName) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recording = CapsuleRecordingContext.Current;
        if (recording is not null && !request.Headers.Contains(TraceCapsuleHeaders.Session))
        {
            request.Headers.TryAddWithoutValidation(TraceCapsuleHeaders.Session, recording.SessionId ?? recording.TraceId);
        }
        var faultHeader = FaultInjectionContext.Current.ToHeaderValue();
        if (!string.IsNullOrEmpty(faultHeader) && !request.Headers.Contains(TraceCapsuleHeaders.FaultLatency))
        {
            request.Headers.TryAddWithoutValidation(TraceCapsuleHeaders.FaultLatency, faultHeader);
        }

        if (recording is null) return await base.SendAsync(request, cancellationToken);

        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var requestHeaders = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray());
        var timestamp = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // A timeout or connection failure is exactly the kind of thing this tool exists to
        // capture (see the README's own motivating example: PaymentService.ReserveBalance
        // timing out against Balance API) — record it as a call with no response rather than
        // silently dropping it, then let the exception continue propagating unchanged.
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            var bodyBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();

            var replacement = new ByteArrayContent(bodyBytes);
            foreach (var header in response.Content.Headers) replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = replacement;

            recording.ExternalHttpCalls.Add(new ExternalHttpCallRecord
            {
                DependencyName = dependencyName,
                Method = request.Method.Method,
                Url = request.RequestUri?.ToString() ?? "",
                RequestHeaders = requestHeaders,
                RequestBody = requestBody,
                ResponseStatusCode = (int)response.StatusCode,
                ResponseHeaders = response.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray()),
                ResponseBody = System.Text.Encoding.UTF8.GetString(bodyBytes),
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Timestamp = timestamp,
            });
            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            stopwatch.Stop();
            recording.ExternalHttpCalls.Add(new ExternalHttpCallRecord
            {
                DependencyName = dependencyName,
                Method = request.Method.Method,
                Url = request.RequestUri?.ToString() ?? "",
                RequestHeaders = requestHeaders,
                RequestBody = requestBody,
                ResponseStatusCode = 0,
                ResponseBody = $"{ex.GetType().Name}: {ex.Message}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Timestamp = timestamp,
            });
            throw;
        }
    }
}
