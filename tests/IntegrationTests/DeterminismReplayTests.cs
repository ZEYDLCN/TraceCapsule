using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using DistributedTransferDemo;
using TraceCapsule.Core.Capsules;
using TraceCapsule.Core.Determinism;
using TraceCapsule.Core.Model;

namespace TraceCapsule.IntegrationTests;

/// <summary>Proves the README's "Deterministic Replay Problem" story end to end against the
/// real demo: payment-service generates its payment id and timestamp through
/// ITraceCapsuleIdGenerator/ITraceCapsuleClock rather than calling Guid.NewGuid()/
/// DateTime.UtcNow directly, so the values it actually returned get captured — and replaying
/// them via ReplayIdGenerator/ReplayClock reproduces the identical values, not new ones.</summary>
public sealed class DeterminismReplayTests : IAsyncLifetime
{
    private readonly string _capsuleDir = Path.Combine(Path.GetTempPath(), $"tracecapsule-determinism-{Guid.NewGuid():N}");
    private DemoHost.Handles? _handles;
    private string _paymentUrl = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_capsuleDir);
        var transferPort = GetFreePort();
        var fraudPort = GetFreePort();
        var paymentPort = GetFreePort();
        _paymentUrl = $"http://127.0.0.1:{paymentPort}";
        _handles = await DemoHost.StartAsync(
            $"http://127.0.0.1:{transferPort}", $"http://127.0.0.1:{fraudPort}", _paymentUrl, _capsuleDir);
    }

    public async Task DisposeAsync()
    {
        if (_handles is not null) await _handles.DisposeAsync();
        if (Directory.Exists(_capsuleDir)) Directory.Delete(_capsuleDir, recursive: true);
    }

    [Fact]
    public async Task Recorded_capsule_captures_the_exact_payment_id_and_timestamp_the_service_returned()
    {
        using var client = new HttpClient();
        var response = await client.PostAsJsonAsync($"{_paymentUrl}/payments/reserve", new TransferRequest("ACC-102", "ACC-550", 100));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var returnedPaymentId = body.GetProperty("paymentId").GetGuid();
        var returnedProcessedAt = body.GetProperty("processedAt").GetDateTimeOffset();

        var capsuleFile = await WaitForCapsuleAsync();
        var capsule = await CapsuleReader.ReadAsync(capsuleFile);

        Assert.Contains(capsule.Determinism, e => e.Kind == DeterminismKind.Guid && e.Value == returnedPaymentId.ToString());
        Assert.Contains(capsule.Determinism, e => e.Kind == DeterminismKind.Clock && DateTimeOffset.Parse(e.Value) == returnedProcessedAt);

        // The actual point of recording these: replaying them reproduces the *same* values,
        // not new ones — a raw Guid.NewGuid()/DateTime.UtcNow call could never do this.
        var replayIds = new ReplayIdGenerator(capsule.Determinism);
        var replayClock = new ReplayClock(capsule.Determinism);
        Assert.Equal(returnedPaymentId, replayIds.NewGuid());
        Assert.Equal(returnedProcessedAt, replayClock.UtcNow);
    }

    private async Task<string> WaitForCapsuleAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var files = Directory.GetFiles(_capsuleDir, "*.capsule");
            if (files.Length > 0) return files[0];
            await Task.Delay(100);
        }
        throw new TimeoutException($"No capsule appeared in {_capsuleDir}.");
    }

    private static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
