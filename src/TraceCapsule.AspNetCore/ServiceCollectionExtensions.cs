using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TraceCapsule.Core.Determinism;
using TraceCapsule.Core.Model;

namespace TraceCapsule.AspNetCore;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="TraceCapsuleOptions"/> and the default (recording)
    /// <see cref="ITraceCapsuleClock"/>/<see cref="ITraceCapsuleIdGenerator"/>/
    /// <see cref="ITraceCapsuleRandom"/> implementations. Pair with
    /// <see cref="ApplicationBuilderExtensions.UseTraceCapsule"/> to actually install the
    /// recording middleware.
    /// <code>
    /// builder.Services.AddTraceCapsule(options =>
    /// {
    ///     options.EnableHttpRecording = true;
    ///     options.EnableOpenTelemetry = true;
    ///     options.EnableRedaction = true;
    /// });
    /// app.UseTraceCapsule();
    /// </code>
    /// </summary>
    public static IServiceCollection AddTraceCapsule(this IServiceCollection services, Action<TraceCapsuleOptions>? configure = null)
    {
        services.AddOptions<TraceCapsuleOptions>();
        if (configure is not null) services.Configure(configure);
        services.TryAddSingleton<ITraceCapsuleClock, RecordingClock>();
        services.TryAddSingleton<ITraceCapsuleIdGenerator, RecordingIdGenerator>();
        services.TryAddSingleton<ITraceCapsuleRandom, RecordingRandom>();
        return services;
    }

    /// <summary>Flips <see cref="ITraceCapsuleClock"/>/<see cref="ITraceCapsuleIdGenerator"/>/
    /// <see cref="ITraceCapsuleRandom"/> into replay mode: instead of generating new values,
    /// they hand back exactly what <paramref name="capsule"/> recorded, in the order it
    /// recorded them (see the README's "Deterministic Replay Problem"). Call this *after*
    /// <see cref="AddTraceCapsule"/> so it overrides the default recording registrations.</summary>
    public static IServiceCollection AddTraceCapsuleDeterminismReplay(this IServiceCollection services, Capsule capsule)
    {
        services.AddSingleton<ITraceCapsuleClock>(new ReplayClock(capsule.Determinism));
        services.AddSingleton<ITraceCapsuleIdGenerator>(new ReplayIdGenerator(capsule.Determinism));
        services.AddSingleton<ITraceCapsuleRandom>(new ReplayRandom(capsule.Determinism));
        return services;
    }
}

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseTraceCapsule(this IApplicationBuilder app) =>
        app.UseMiddleware<TraceCapsuleMiddleware>();
}
