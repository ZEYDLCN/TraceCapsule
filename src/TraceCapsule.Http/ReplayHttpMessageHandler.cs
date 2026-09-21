using System.Net;
using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Http;

/// <summary>Phase 3 replay side (with Phase 6 fault injection layered on top): serves
/// responses recorded by <see cref="RecordingHttpMessageHandler"/> instead of making a real
/// network call, so replaying a capsule never depends on the real dependency's availability,
/// network state, or API version. Register this in place of the real handler when the host
/// app is running in "replay mode":
/// <code>
/// services.AddHttpClient("fraud-api")
///     .AddTraceCapsuleReplay(dependencyName: "fraud-api", () => recordedCalls);
/// </code>
/// </summary>
public sealed class ReplayHttpMessageHandler(IEnumerable<ExternalHttpCallRecord> recordedCalls, string dependencyName) : HttpMessageHandler
{
    private readonly List<ExternalHttpCallRecord> _remaining = recordedCalls
        .Where(c => string.Equals(c.DependencyName, dependencyName, StringComparison.OrdinalIgnoreCase))
        .ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var fault = FaultInjectionContext.Current;
        fault.TryGetFault(dependencyName, out var spec);
        if (spec.ExtraLatencyMs is > 0)
        {
            await Task.Delay(spec.ExtraLatencyMs.Value, cancellationToken);
        }

        var path = request.RequestUri?.PathAndQuery ?? "";
        var match = FindAndConsumeMatch(request.Method.Method, path);
        if (match is null)
        {
            throw new TraceCapsuleReplayException(
                $"No recorded external-http call for '{dependencyName}' {request.Method} {path}. " +
                "Either the capsule doesn't include this dependency, or replay diverged from the " +
                "recorded execution path.");
        }

        var statusCode = spec.ForcedStatusCode ?? match.ResponseStatusCode;
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        if (match.ResponseBody is not null) response.Content = new StringContent(match.ResponseBody);
        foreach (var (name, values) in match.ResponseHeaders)
        {
            var value = string.Join(",", values);
            if (!response.Headers.TryAddWithoutValidation(name, value))
            {
                response.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }
        return response;
    }

    private ExternalHttpCallRecord? FindAndConsumeMatch(string method, string path)
    {
        var match = _remaining.FirstOrDefault(call =>
            string.Equals(call.Method, method, StringComparison.OrdinalIgnoreCase) && MatchesPath(call.Url, path));
        if (match is not null) _remaining.Remove(match);
        return match;
    }

    private static bool MatchesPath(string recordedUrl, string requestedPathAndQuery) =>
        Uri.TryCreate(recordedUrl, UriKind.Absolute, out var recorded)
            ? recorded.PathAndQuery == requestedPathAndQuery
            : recordedUrl.Contains(requestedPathAndQuery, StringComparison.OrdinalIgnoreCase);
}

public sealed class TraceCapsuleReplayException(string message) : Exception(message);
