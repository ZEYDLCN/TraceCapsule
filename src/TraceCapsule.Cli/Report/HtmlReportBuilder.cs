using System.Net;
using System.Text;
using TraceCapsule.Core.Analysis;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Cli.Report;

/// <summary>Renders a capsule as a single, self-contained HTML file — no external
/// stylesheets, scripts or fonts — so `tracecapsule inspect --html` produces something you
/// can open directly in a browser or attach to an incident ticket, instead of only ever
/// reading text in a terminal. Everything pulled from the capsule (span names, URLs, bodies,
/// exception messages, ...) is HTML-encoded before being written out: capsule content comes
/// from a production request and must be treated as untrusted when rendering it as markup.</summary>
public static class HtmlReportBuilder
{
    public static string Build(Capsule capsule, IncidentAnalysis analysis)
    {
        var sb = new StringBuilder();
        var isError = capsule.Exceptions.Count > 0 || (capsule.Response?.StatusCode ?? 200) >= 500;

        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>TraceCapsule — ").Append(H(capsule.Metadata.TraceId)).Append("</title>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<style>").Append(Css).Append("</style></head><body>");

        AppendHeader(sb, capsule, isError);
        AppendMetrics(sb, capsule);
        if (analysis.Findings.Count > 0) AppendAnalysis(sb, analysis);
        if (capsule.Exceptions.Count > 0) AppendExceptions(sb, capsule.Exceptions);
        if (capsule.Trace.Count > 0) AppendWaterfall(sb, capsule);
        if (capsule.ExternalHttpCalls.Count > 0) AppendExternalCalls(sb, capsule.ExternalHttpCalls);
        if (capsule.Events.Count > 0) AppendEvents(sb, capsule.Events);
        AppendRequestResponse(sb, capsule);

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, Capsule capsule, bool isError)
    {
        sb.Append("<header class=\"top\"><div class=\"brand\">TraceCapsule</div>");
        sb.Append("<h1>").Append(H(capsule.Metadata.Request.Method)).Append(' ').Append(H(capsule.Metadata.Request.Path)).Append("</h1>");
        sb.Append("<div class=\"sub\">trace <code>").Append(H(capsule.Metadata.TraceId)).Append("</code> · ")
          .Append(H(capsule.Metadata.Timestamp.ToString("u"))).Append("</div>");
        sb.Append("<span class=\"badge ").Append(isError ? "bad" : "good").Append("\">")
          .Append(capsule.Response is { } r ? $"HTTP {r.StatusCode}" : (isError ? "FAILED" : "OK")).Append("</span>");
        sb.Append("</header>");
    }

    private static void AppendMetrics(StringBuilder sb, Capsule capsule)
    {
        sb.Append("<section class=\"metrics\">");
        Metric(sb, "Duration", capsule.Timing is null ? "—" : $"{capsule.Timing.TotalDurationMs:F0} ms");
        Metric(sb, "Services", string.Join(", ", capsule.Metadata.Services).NullIfEmpty() ?? "—");
        Metric(sb, "Spans", capsule.Trace.Count.ToString());
        Metric(sb, "External calls", capsule.ExternalHttpCalls.Count.ToString());
        Metric(sb, "Queue events", capsule.Events.Count.ToString());
        Metric(sb, "Exceptions", capsule.Exceptions.Count.ToString());
        sb.Append("</section>");
    }

    private static void Metric(StringBuilder sb, string label, string value) =>
        sb.Append("<div class=\"metric\"><span>").Append(H(label)).Append("</span><strong>").Append(H(value)).Append("</strong></div>");

    private static void AppendAnalysis(StringBuilder sb, IncidentAnalysis analysis)
    {
        sb.Append("<section class=\"panel\"><h2>Analysis</h2><ul class=\"findings\">");
        foreach (var finding in analysis.Findings)
        {
            sb.Append("<li><strong>").Append(H(finding.Title)).Append(":</strong> ").Append(H(finding.Detail)).Append("</li>");
        }
        sb.Append("</ul></section>");
    }

    private static void AppendExceptions(StringBuilder sb, IReadOnlyList<ExceptionRecord> exceptions)
    {
        sb.Append("<section class=\"panel error\"><h2>Exceptions</h2>");
        foreach (var exception in exceptions)
        {
            sb.Append("<details open><summary>").Append(H(exception.Type)).Append(": ").Append(H(exception.Message)).Append("</summary>");
            if (!string.IsNullOrEmpty(exception.StackTrace)) sb.Append("<pre>").Append(H(exception.StackTrace)).Append("</pre>");
            sb.Append("</details>");
        }
        sb.Append("</section>");
    }

    private static void AppendWaterfall(StringBuilder sb, Capsule capsule)
    {
        var start = capsule.Trace.Min(s => s.StartTime);
        var totalMs = Math.Max(Math.Max(capsule.Timing?.TotalDurationMs ?? 0, capsule.Trace.Max(s => (s.EndTime - start).TotalMilliseconds)), 1);

        sb.Append("<section class=\"panel\"><h2>Trace</h2><div class=\"waterfall\">");
        foreach (var span in capsule.Trace.OrderBy(s => s.StartTime))
        {
            var offsetPct = Math.Clamp((span.StartTime - start).TotalMilliseconds / totalMs * 100, 0, 100);
            var widthPct = Math.Clamp(span.DurationMs / totalMs * 100, 0.3, 100 - offsetPct);
            var errorClass = span.Status == "Error" ? " error" : "";
            sb.Append("<div class=\"span-row\">")
              .Append("<div class=\"span-label\">").Append(H(span.ServiceName)).Append('.').Append(H(span.Name)).Append("</div>")
              .Append("<div class=\"span-track\"><div class=\"span-bar").Append(errorClass).Append("\" style=\"left:")
              .Append(offsetPct.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append("%;width:")
              .Append(widthPct.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append("%\" title=\"")
              .Append(H($"{span.DurationMs:F0} ms")).Append("\"></div></div>")
              .Append("<div class=\"span-duration\">").Append(H($"{span.DurationMs:F0} ms")).Append("</div>")
              .Append("</div>");
        }
        sb.Append("</div></section>");
    }

    private static void AppendExternalCalls(StringBuilder sb, IReadOnlyList<ExternalHttpCallRecord> calls)
    {
        sb.Append("<section class=\"panel\"><h2>External calls</h2><table><thead><tr>")
          .Append("<th>Dependency</th><th>Method</th><th>URL</th><th>Status</th><th>Duration</th></tr></thead><tbody>");
        foreach (var call in calls)
        {
            var failed = call.ResponseStatusCode == 0 || call.ResponseStatusCode >= 500;
            sb.Append("<tr class=\"").Append(failed ? "error" : "").Append("\"><td>").Append(H(call.DependencyName)).Append("</td><td>")
              .Append(H(call.Method)).Append("</td><td class=\"url\">").Append(H(call.Url)).Append("</td><td>")
              .Append(call.ResponseStatusCode == 0 ? "no response" : call.ResponseStatusCode.ToString()).Append("</td><td>")
              .Append(H($"{call.DurationMs:F0} ms")).Append("</td></tr>");
        }
        sb.Append("</tbody></table></section>");
    }

    private static void AppendEvents(StringBuilder sb, IReadOnlyList<QueueEventRecord> events)
    {
        sb.Append("<section class=\"panel\"><h2>Queue events</h2><table><thead><tr>")
          .Append("<th>Direction</th><th>Type</th><th>Queue / Routing key</th><th>Message id</th><th>Timestamp</th></tr></thead><tbody>");
        foreach (var evt in events.OrderBy(e => e.Timestamp))
        {
            sb.Append("<tr><td>").Append(H(evt.Direction.ToString())).Append("</td><td>").Append(H(evt.EventType)).Append("</td><td>")
              .Append(H(string.IsNullOrEmpty(evt.Queue) ? evt.RoutingKey : evt.Queue)).Append("</td><td>").Append(H(evt.MessageId ?? "—"))
              .Append("</td><td>").Append(H(evt.Timestamp.ToString("u"))).Append("</td></tr>");
        }
        sb.Append("</tbody></table></section>");
    }

    private static void AppendRequestResponse(StringBuilder sb, Capsule capsule)
    {
        sb.Append("<section class=\"panel\"><h2>Request / Response</h2><div class=\"grid2\">");
        AppendBodyColumn(sb, "Request", capsule.Request?.Headers, capsule.Request?.Body);
        AppendBodyColumn(sb, "Response", capsule.Response?.Headers, capsule.Response?.Body);
        sb.Append("</div></section>");
    }

    private static void AppendBodyColumn(StringBuilder sb, string title, Dictionary<string, string[]>? headers, string? body)
    {
        sb.Append("<div><h3>").Append(H(title)).Append("</h3>");
        if (headers is { Count: > 0 })
        {
            sb.Append("<details><summary>Headers (").Append(headers.Count).Append(")</summary><pre>");
            foreach (var (name, values) in headers) sb.Append(H(name)).Append(": ").Append(H(string.Join(", ", values))).Append('\n');
            sb.Append("</pre></details>");
        }
        sb.Append("<pre class=\"body\">").Append(string.IsNullOrEmpty(body) ? "(empty)" : H(body)).Append("</pre></div>");
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string? NullIfEmpty(this string value) => string.IsNullOrEmpty(value) ? null : value;

    private const string Css = """
        :root { color-scheme: dark; }
        * { box-sizing: border-box; }
        body { margin: 0; padding: 24px; background: #0b1615; color: #dce8e5; font-family: -apple-system, "Segoe UI", Inter, sans-serif; line-height: 1.5; }
        code, pre { font-family: "Cascadia Code", Consolas, monospace; }
        header.top { display: flex; flex-wrap: wrap; align-items: baseline; gap: 10px 16px; padding-bottom: 16px; border-bottom: 1px solid #1e3733; }
        .brand { font-weight: 800; letter-spacing: -.02em; color: #56d6c0; flex-basis: 100%; font-size: 13px; text-transform: uppercase; }
        header.top h1 { margin: 0; font-size: 22px; }
        .sub { color: #7fa39b; font-size: 13px; }
        .sub code { background: #10201d; padding: 2px 6px; border-radius: 6px; }
        .badge { margin-left: auto; padding: 6px 12px; border-radius: 999px; font-weight: 800; font-size: 12px; }
        .badge.good { background: #12331f; color: #6bdc9a; }
        .badge.bad { background: #3a1616; color: #ff8c82; }
        .metrics { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 12px; margin: 20px 0; }
        .metric { padding: 12px 14px; background: #0f1f1c; border: 1px solid #1e3733; border-radius: 12px; }
        .metric span { display: block; font-size: 10px; text-transform: uppercase; letter-spacing: .06em; color: #7fa39b; }
        .metric strong { font-size: 18px; }
        .panel { margin: 20px 0; padding: 16px 18px; background: #0f1f1c; border: 1px solid #1e3733; border-radius: 14px; }
        .panel.error { border-color: #5c2626; background: #1c0f0f; }
        .panel h2 { margin: 0 0 12px; font-size: 15px; }
        .findings { margin: 0; padding-left: 18px; }
        .findings li { margin-bottom: 6px; }
        details summary { cursor: pointer; color: #a8c4bf; }
        pre { white-space: pre-wrap; word-break: break-word; background: #081311; padding: 10px 12px; border-radius: 8px; font-size: 12px; max-height: 320px; overflow: auto; }
        table { width: 100%; border-collapse: collapse; font-size: 13px; }
        th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid #1e3733; }
        th { color: #7fa39b; font-weight: 700; font-size: 11px; text-transform: uppercase; }
        tr.error td { color: #ff8c82; }
        td.url { max-width: 360px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .waterfall { display: flex; flex-direction: column; gap: 6px; }
        .span-row { display: grid; grid-template-columns: 220px 1fr 70px; align-items: center; gap: 10px; font-size: 12px; }
        .span-label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #cfe4df; }
        .span-track { position: relative; height: 16px; background: #081311; border-radius: 4px; }
        .span-bar { position: absolute; top: 0; height: 100%; min-width: 3px; border-radius: 4px; background: #2f9a86; }
        .span-bar.error { background: #d9534f; }
        .span-duration { text-align: right; color: #7fa39b; }
        .grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
        @media (max-width: 720px) { .grid2 { grid-template-columns: 1fr; } .span-row { grid-template-columns: 120px 1fr 60px; } }
        """;
}
