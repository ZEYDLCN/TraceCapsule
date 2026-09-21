using System.Diagnostics;
using TraceCapsule.Core.Model;
using TraceCapsule.Core.Recording;

namespace TraceCapsule.Http;

/// <summary>Phase 3: wraps an outbound <c>HttpClient</c> pipeline and records every call
/// into the ambient <see cref="CapsuleRecordingContext"/> as an
/// <see cref="ExternalHttpCallRecord"/>, so it can be replayed later without hitting the
/// real dependency (<see cref="ReplayHttpMessageHandler"/>). No-ops if there's no capsule
/// currently being recorded (e.g. a background job outside any request).
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
        if (recording is null) return await base.SendAsync(request, cancellationToken);

        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

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
            RequestHeaders = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray()),
            RequestBody = requestBody,
            ResponseStatusCode = (int)response.StatusCode,
            ResponseHeaders = response.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray()),
            ResponseBody = System.Text.Encoding.UTF8.GetString(bodyBytes),
            DurationMs = stopwatch.Elapsed.TotalMilliseconds,
            Timestamp = timestamp,
        });

        return response;
    }
}
