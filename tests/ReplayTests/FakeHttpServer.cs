using System.Net;
using System.Text;

namespace TraceCapsule.ReplayTests;

/// <summary>A tiny single-request loopback HTTP server, so <c>CapsuleReplayer</c> tests
/// exercise a real socket round-trip without pulling in a full ASP.NET Core test host.</summary>
public sealed class FakeHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();

    public Uri BaseUrl { get; }
    public string? LastReceivedFaultHeader { get; private set; }
    public string? LastReceivedPath { get; private set; }
    public string? LastReceivedBody { get; private set; }
    public string? LastReceivedContentType { get; private set; }

    public FakeHttpServer()
    {
        var port = GetFreePort();
        BaseUrl = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(BaseUrl.ToString());
        _listener.Start();
    }

    /// <summary>Handles exactly one request and responds with the given status/body.</summary>
    public async Task RespondOnceAsync(int statusCode, string body, string contentType = "application/json")
    {
        var context = await _listener.GetContextAsync();
        LastReceivedPath = context.Request.Url?.PathAndQuery;
        LastReceivedFaultHeader = context.Request.Headers["X-TraceCapsule-Fault-Latency"];
        LastReceivedContentType = context.Request.ContentType;
        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            LastReceivedBody = await reader.ReadToEndAsync();
        }
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}
