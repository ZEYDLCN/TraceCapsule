using Microsoft.Extensions.DependencyInjection;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Http;

public static class HttpClientBuilderExtensions
{
    /// <summary>Records every call this named <c>HttpClient</c> makes into the ambient
    /// capsule (Phase 3).</summary>
    public static IHttpClientBuilder AddTraceCapsuleRecording(this IHttpClientBuilder builder, string dependencyName) =>
        builder.AddHttpMessageHandler(() => new RecordingHttpMessageHandler(dependencyName));

    /// <summary>Swaps this named <c>HttpClient</c>'s handler pipeline for one that serves
    /// responses out of <paramref name="recordedCallsProvider"/> instead of making real
    /// network calls — this is what "replay mode" means for outbound dependencies. The
    /// provider is invoked lazily so the capsule only needs to be loaded once replay
    /// actually starts, not at DI-registration time.</summary>
    public static IHttpClientBuilder AddTraceCapsuleReplay(
        this IHttpClientBuilder builder, string dependencyName, Func<IEnumerable<ExternalHttpCallRecord>> recordedCallsProvider) =>
        builder.ConfigurePrimaryHttpMessageHandler(() => new ReplayHttpMessageHandler(recordedCallsProvider(), dependencyName));

    /// <summary>Phase 8's one-call test-environment setup: registers a named
    /// <c>HttpClient</c> wired to <see cref="AddTraceCapsuleReplay"/> for every dependency
    /// <paramref name="capsule"/> actually recorded an external call for, so a test host
    /// (an xUnit fixture, a <c>WebApplicationFactory</c>, ...) doesn't have to know the
    /// dependency names up front or call <see cref="AddTraceCapsuleReplay"/> once per
    /// dependency. The application still resolves these the normal way — an
    /// <c>IHttpClientFactory</c> asking for <c>"fraud-api"</c> — it's just talking to
    /// recorded responses instead of the network. Call this from the test host's DI setup in
    /// place of whatever registers the real named clients when replaying a capsule.</summary>
    public static IServiceCollection AddTraceCapsuleReplayEnvironment(this IServiceCollection services, Capsule capsule)
    {
        foreach (var dependencyName in capsule.ExternalHttpCalls.Select(c => c.DependencyName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            services.AddHttpClient(dependencyName)
                .AddTraceCapsuleReplay(dependencyName, () => capsule.ExternalHttpCalls);
        }
        return services;
    }
}
