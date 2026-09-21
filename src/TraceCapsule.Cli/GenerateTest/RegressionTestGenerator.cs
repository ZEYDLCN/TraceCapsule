using System.Text;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Cli.GenerateTest;

/// <summary>Phase 8's <c>tracecapsule generate-test</c>: turns two capsules — the original
/// production failure and a replay result recorded once the fix was verified locally — into
/// a self-contained xUnit test file that proves the bug stays fixed. The generated test has
/// no dependency on this CLI or on any capsule file at run time: the recorded request, the
/// original failure signature, and the now-expected response are all baked into the source
/// as literals, so it's just another test in the suite, safe to commit and run in CI like any
/// other. It talks to a live target over HTTP (the same model as <c>tracecapsule replay</c>),
/// reading the base URL from an environment variable so CI can point it at whatever instance
/// it just started.</summary>
public static class RegressionTestGenerator
{
    public static string Generate(
        Capsule bug,
        Capsule fixedCapsule,
        string @namespace,
        string className,
        IReadOnlyList<string> ignoreBodyPaths,
        string targetUrlEnvironmentVariable)
    {
        var request = bug.Request ?? throw new InvalidOperationException(
            "The bug capsule has no recorded request (request.json is missing) — nothing to generate a test from.");
        if (fixedCapsule.Response is null) throw new InvalidOperationException(
            "The fixed capsule has no recorded response — replay the bug capsule against the fixed target first (`tracecapsule replay`), then pass that result here.");

        var expectedStatus = fixedCapsule.Response.StatusCode;
        var expectedBody = fixedCapsule.Response.Body;
        var originalStatus = bug.Response?.StatusCode;
        var originalExceptionType = bug.Exceptions.FirstOrDefault()?.Type;

        var sb = new StringBuilder();
        AppendHeaderComment(sb, bug, targetUrlEnvironmentVariable);
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Net.Http;");
        sb.AppendLine("using System.Net.Http.Headers;");
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using TraceCapsule.Core.Comparison;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine();
        sb.AppendLine($"namespace {@namespace};");
        sb.AppendLine();
        sb.AppendLine($"public class {className}");
        sb.AppendLine("{");
        sb.AppendLine($"    private static readonly string TargetUrl = Environment.GetEnvironmentVariable({Literal(targetUrlEnvironmentVariable)}) ?? \"http://localhost:5000\";");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine($"    public async Task Bug_{SanitizeIdentifier(bug.Metadata.TraceId)}_no_longer_reproduces()");
        sb.AppendLine("    {");
        sb.AppendLine("        using var client = new HttpClient { BaseAddress = new Uri(TargetUrl) };");
        sb.AppendLine($"        using var request = new HttpRequestMessage(new HttpMethod({Literal(request.Method)}), {Literal(request.Path + request.QueryString)});");
        if (!string.IsNullOrEmpty(request.Body))
        {
            sb.AppendLine($"        var contentType = MediaTypeHeaderValue.Parse({Literal(request.ContentType ?? "application/json")});");
            sb.AppendLine("""        var encoding = string.IsNullOrEmpty(contentType.CharSet) ? Encoding.UTF8 : Encoding.GetEncoding(contentType.CharSet.Trim('"'));""");
            sb.AppendLine($"        request.Content = new StringContent({Literal(request.Body)}, encoding);");
            sb.AppendLine("        request.Content.Headers.ContentType = contentType;");
        }
        sb.AppendLine();
        sb.AppendLine("        using var response = await client.SendAsync(request);");
        sb.AppendLine("        var body = await response.Content.ReadAsStringAsync();");
        sb.AppendLine();
        AppendFailureSignatureAssertion(sb, originalStatus, expectedStatus, originalExceptionType);
        sb.AppendLine($"        Assert.Equal({expectedStatus}, (int)response.StatusCode);");

        if (expectedBody is not null)
        {
            sb.AppendLine();
            sb.AppendLine("        // Structural diff, not byte-for-byte: fields that are expected to vary between");
            sb.AppendLine("        // runs (timestamps, generated ids, ...) were excluded when this test was generated.");
            sb.AppendLine("        var differences = JsonBodyDiff.Compare(");
            sb.AppendLine($"            expectedJson: {Literal(expectedBody)},");
            sb.AppendLine("            actualJson: body,");
            sb.AppendLine($"            ignoredPaths: [{string.Join(", ", ignoreBodyPaths.Select(Literal))}]);");
            sb.AppendLine("        Assert.Empty(differences);");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendHeaderComment(StringBuilder sb, Capsule bug, string targetUrlEnvironmentVariable)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"//   Generated by `tracecapsule generate-test` from trace {bug.Metadata.TraceId}.");
        sb.AppendLine($"//   Replays the original production request against a live target read from the");
        sb.AppendLine($"//   {targetUrlEnvironmentVariable} environment variable (default http://localhost:5000),");
        sb.AppendLine("//   and asserts the originally recorded failure no longer reproduces.");
        if (bug.ExternalHttpCalls.Count > 0)
        {
            sb.AppendLine("//");
            sb.AppendLine("//   The target must be running with these dependencies replayed from the same capsule");
            sb.AppendLine("//   (TraceCapsule.Http's AddTraceCapsuleReplayEnvironment(capsule)), otherwise it will");
            sb.AppendLine("//   try to reach the real network for:");
            foreach (var dependency in bug.ExternalHttpCalls.Select(c => c.DependencyName).Distinct(StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"//     - {dependency}");
        }
        sb.AppendLine("// </auto-generated>");
    }

    private static void AppendFailureSignatureAssertion(StringBuilder sb, int? originalStatus, int expectedStatus, string? originalExceptionType)
    {
        if (originalStatus is { } status)
        {
            sb.AppendLine($"        // Production originally returned HTTP {status}{DescribeException(originalExceptionType)} for this request.");
            // A business-logic fix can change the body while keeping HTTP 200. In that
            // case the expected-body assertion identifies the regression.
            if (status != expectedStatus)
                sb.AppendLine($"        Assert.NotEqual({status}, (int)response.StatusCode);");
        }
        else
        {
            sb.AppendLine($"        // Production originally failed with no response{DescribeException(originalExceptionType)} for this request.");
        }
    }

    private static string DescribeException(string? exceptionType) => exceptionType is null ? "" : $" ({exceptionType})";

    /// <summary>Turns an arbitrary string (a trace id, typically) into a valid C# identifier
    /// fragment — every non-alphanumeric character becomes <c>_</c>, and a leading digit gets
    /// an underscore in front of it. Exposed so the CLI can derive a default class name from
    /// the same rule this class uses for the generated test method's name.</summary>
    public static string SanitizeIdentifier(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var candidate = new string(chars);
        return candidate.Length == 0 || char.IsDigit(candidate[0]) ? "_" + candidate : candidate;
    }

    private static string Literal(string? value)
    {
        if (value is null) return "null";
        var sb = new StringBuilder("\"");
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append($"\\u{(int)ch:x4}");
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
