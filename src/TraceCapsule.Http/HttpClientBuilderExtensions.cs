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
}
