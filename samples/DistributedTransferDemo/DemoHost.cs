using System.Net.Http.Json;
using TraceCapsule.AspNetCore;
using TraceCapsule.Core.Determinism;
using TraceCapsule.Http;
using TraceCapsule.OpenTelemetry;

namespace DistributedTransferDemo;

public sealed record TransferRequest(string FromAccount, string ToAccount, decimal Amount);

/// <summary>Phase 5's demo: three real ASP.NET Core services (Transfer -> Fraud, Transfer ->
/// Payment) hosted in one process on three loopback ports, each independently wired with
/// TraceCapsule. Transfer is the entry point; it never sees Fraud or Payment's own recording —
/// each writes its own partial capsule to the shared <c>capsuleDir</c>, correlated by the
/// <c>X-TraceCapsule-Session</c> header that <c>RecordingHttpMessageHandler</c> forwards
/// automatically. Run `tracecapsule merge &lt;session-id&gt; --from &lt;capsuleDir&gt;`
/// afterwards to combine them — see the README's "Demo Application" section.
///
/// Payment has an intentional bug baked in (matching the README): transfers of 5000 or more
/// take far longer than Transfer's client timeout allows, so the demo reliably reproduces a
/// timeout without any manual fault injection.</summary>
public static class DemoHost
{
    public sealed class Handles(WebApplication transfer, WebApplication fraud, WebApplication payment) : IAsyncDisposable
    {
        public WebApplication Transfer { get; } = transfer;
        public WebApplication Fraud { get; } = fraud;
        public WebApplication Payment { get; } = payment;

        public async ValueTask DisposeAsync()
        {
            await Transfer.DisposeAsync();
            await Fraud.DisposeAsync();
            await Payment.DisposeAsync();
        }
    }

    public static async Task<Handles> StartAsync(string transferUrl, string fraudUrl, string paymentUrl, string capsuleDir)
    {
        CapsuleActivityListener.Enable();

        var fraud = BuildFraudApp(fraudUrl, capsuleDir);
        var payment = BuildPaymentApp(paymentUrl, capsuleDir);
        var transfer = BuildTransferApp(transferUrl, capsuleDir, fraudUrl, paymentUrl);

        await fraud.StartAsync();
        await payment.StartAsync();
        await transfer.StartAsync();

        return new Handles(transfer, fraud, payment);
    }

    private static WebApplication BuildFraudApp(string url, string capsuleDir)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(url);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddTraceCapsule(o =>
        {
            o.OutputDirectory = capsuleDir;
            o.AppVersion = "fraud-service";
            o.CapturePolicy.SamplingRate = 1.0;
        });
        var app = builder.Build();
        app.UseTraceCapsule();
        app.MapPost("/fraud/check", async (TransferRequest body) =>
        {
            await Task.Delay(30);
            return Results.Ok(new { riskScore = 12 });
        });
        return app;
    }

    private static WebApplication BuildPaymentApp(string url, string capsuleDir)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(url);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddTraceCapsule(o =>
        {
            o.OutputDirectory = capsuleDir;
            o.AppVersion = "payment-service";
            o.CapturePolicy.SamplingRate = 1.0;
        });
        var app = builder.Build();
        app.UseTraceCapsule();
        app.MapPost("/payments/reserve", async (TransferRequest body, ITraceCapsuleIdGenerator idGenerator, ITraceCapsuleClock clock) =>
        {
            // Uses the TraceCapsule determinism abstractions (not Guid.NewGuid()/DateTime.UtcNow
            // directly) so a replay of this capsule reproduces the exact same payment id and
            // timestamp instead of generating new ones — see the README's "Deterministic
            // Replay Problem" and docs/roadmap.md's Advanced Features note.
            var paymentId = idGenerator.NewGuid();
            var processedAt = clock.UtcNow;
            if (body.Amount >= 5000) await Task.Delay(TimeSpan.FromSeconds(5));
            return Results.Ok(new { reserved = true, paymentId, processedAt });
        });
        return app;
    }

    private static WebApplication BuildTransferApp(string url, string capsuleDir, string fraudBaseUrl, string paymentBaseUrl)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(url);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddTraceCapsule(o =>
        {
            o.OutputDirectory = capsuleDir;
            o.AppVersion = "transfer-service";
            o.CapturePolicy.SamplingRate = 1.0;
        });
        builder.Services.AddHttpClient("fraud-api", client => client.BaseAddress = new Uri(fraudBaseUrl))
            .AddTraceCapsuleRecording("fraud-api");
        builder.Services.AddHttpClient("payment-api", client =>
        {
            client.BaseAddress = new Uri(paymentBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
        }).AddTraceCapsuleRecording("payment-api");

        var app = builder.Build();
        app.UseTraceCapsule();
        app.MapPost("/api/transfers", async (TransferRequest body, IHttpClientFactory httpClientFactory) =>
        {
            var fraudResponse = await httpClientFactory.CreateClient("fraud-api").PostAsJsonAsync("/fraud/check", body);
            fraudResponse.EnsureSuccessStatusCode();

            var paymentResponse = await httpClientFactory.CreateClient("payment-api").PostAsJsonAsync("/payments/reserve", body);
            paymentResponse.EnsureSuccessStatusCode();

            return Results.Ok(new { status = "completed" });
        });
        return app;
    }
}
