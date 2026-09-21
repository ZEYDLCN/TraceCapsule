using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using DistributedTransferDemo;
using TraceCapsule.Core.Capsules;

namespace TraceCapsule.IntegrationTests;

public sealed class DistributedTransferDemoTests : IAsyncLifetime
{
    private readonly string _capsuleDir = Path.Combine(Path.GetTempPath(), $"tracecapsule-distributed-{Guid.NewGuid():N}");
    private DemoHost.Handles? _handles;
    private string _transferUrl = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_capsuleDir);
        var transferPort = GetFreePort();
        var fraudPort = GetFreePort();
        var paymentPort = GetFreePort();
        _transferUrl = $"http://127.0.0.1:{transferPort}";
        _handles = await DemoHost.StartAsync(
            _transferUrl, $"http://127.0.0.1:{fraudPort}", $"http://127.0.0.1:{paymentPort}", _capsuleDir);
    }

    public async Task DisposeAsync()
    {
        if (_handles is not null) await _handles.DisposeAsync();
        if (Directory.Exists(_capsuleDir)) Directory.Delete(_capsuleDir, recursive: true);
    }

    [Fact]
    public async Task A_normal_transfer_produces_three_capsules_sharing_one_session_id()
    {
        using var client = new HttpClient();
        var response = await client.PostAsJsonAsync($"{_transferUrl}/api/transfers",
            new TransferRequest("ACC-102", "ACC-550", 100));
        response.EnsureSuccessStatusCode();

        var capsules = await WaitForCapsulesAsync(expectedCount: 3);

        var sessionIds = capsules.Select(c => c.Metadata.SessionId).Distinct().ToList();
        var sessionId = Assert.Single(sessionIds);
        Assert.NotNull(sessionId);

        var services = capsules.SelectMany(c => c.Metadata.Services).OrderBy(s => s).ToList();
        Assert.Equal(["fraud-service", "payment-service", "transfer-service"], services);

        var merged = CapsuleMerger.Merge(capsules);
        Assert.Equal(3, merged.Metadata.Services.Count);
        Assert.Equal(2, merged.ExternalHttpCalls.Count); // transfer-service's calls to fraud-api + payment-api
        Assert.Empty(merged.Exceptions);
    }

    [Fact]
    public async Task A_large_transfer_reproduces_the_payment_timeout_bug()
    {
        using var client = new HttpClient();
        var response = await client.PostAsJsonAsync($"{_transferUrl}/api/transfers",
            new TransferRequest("ACC-102", "ACC-550", 5000));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var capsules = await WaitForCapsulesAsync(expectedCount: 2); // payment-service never gets to write its own capsule in time
        var transferCapsule = Assert.Single(capsules, c => c.Metadata.Services.Contains("transfer-service"));
        Assert.NotEmpty(transferCapsule.Exceptions);
        Assert.Contains(transferCapsule.ExternalHttpCalls, call => call.DependencyName == "payment-api");
    }

    private async Task<List<Core.Model.Capsule>> WaitForCapsulesAsync(int expectedCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var files = Directory.GetFiles(_capsuleDir, "*.capsule");
            if (files.Length >= expectedCount)
            {
                var capsules = new List<Core.Model.Capsule>();
                foreach (var file in files) capsules.Add(await CapsuleReader.ReadAsync(file));
                return capsules;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException($"Expected at least {expectedCount} capsule(s) in {_capsuleDir}, found {Directory.GetFiles(_capsuleDir, "*.capsule").Length}.");
    }

    private static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
