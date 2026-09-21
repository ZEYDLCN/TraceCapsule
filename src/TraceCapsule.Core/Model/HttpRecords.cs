namespace TraceCapsule.Core.Model;

/// <summary>The inbound request that triggered capsule recording.</summary>
public sealed class HttpRequestRecord
{
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string QueryString { get; set; } = "";
    public Dictionary<string, string[]> Headers { get; set; } = new();
    public string? Body { get; set; }
    public string? ContentType { get; set; }
}

/// <summary>The response the application produced for the recorded request.</summary>
public sealed class HttpResponseRecord
{
    public int StatusCode { get; set; }
    public Dictionary<string, string[]> Headers { get; set; } = new();
    public string? Body { get; set; }
    public string? ContentType { get; set; }
}

/// <summary>An outbound call to an external dependency (Phase 3), recorded so it can be
/// replayed without hitting the real dependency again.</summary>
public sealed class ExternalHttpCallRecord
{
    public string DependencyName { get; set; } = "";
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public Dictionary<string, string[]> RequestHeaders { get; set; } = new();
    public string? RequestBody { get; set; }
    public int ResponseStatusCode { get; set; }
    public Dictionary<string, string[]> ResponseHeaders { get; set; } = new();
    public string? ResponseBody { get; set; }
    public double DurationMs { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
