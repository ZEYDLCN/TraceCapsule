using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace TraceCapsule.AspNetCore;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="TraceCapsuleOptions"/>. Pair with
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
        return services;
    }
}

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseTraceCapsule(this IApplicationBuilder app) =>
        app.UseMiddleware<TraceCapsuleMiddleware>();
}
