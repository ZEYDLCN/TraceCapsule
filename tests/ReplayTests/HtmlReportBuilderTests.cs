using TraceCapsule.Cli.Report;
using TraceCapsule.Core.Analysis;
using TraceCapsule.Core.Model;

namespace TraceCapsule.ReplayTests;

public class HtmlReportBuilderTests
{
    private static Capsule BuildCapsule() => new()
    {
        Metadata = new CapsuleMetadata
        {
            TraceId = "trace-abc",
            Timestamp = new DateTimeOffset(2026, 9, 21, 10, 14, 33, TimeSpan.Zero),
            Request = new RequestInfo { Method = "POST", Path = "/api/transfers" },
            Services = ["transfer-service", "payment-service"],
        },
        Response = new HttpResponseRecord { StatusCode = 500 },
        Trace =
        [
            new SpanRecord { SpanId = "s1", ServiceName = "transfer-service", Name = "POST /transfer", StartTime = DateTimeOffset.UnixEpoch, EndTime = DateTimeOffset.UnixEpoch.AddMilliseconds(1321), DurationMs = 1321, Status = "Error" },
            new SpanRecord { SpanId = "s2", ServiceName = "payment-service", Name = "ReserveBalance", StartTime = DateTimeOffset.UnixEpoch.AddMilliseconds(50), EndTime = DateTimeOffset.UnixEpoch.AddMilliseconds(1300), DurationMs = 1250, Status = "Error" },
        ],
        ExternalHttpCalls = [new ExternalHttpCallRecord { DependencyName = "balance-api", Method = "GET", Url = "https://balance/x", ResponseStatusCode = 504, DurationMs = 900 }],
        Exceptions = [new ExceptionRecord { Type = "TimeoutException", Message = "boom", SpanId = "s2", StackTrace = "at X.Y()" }],
        Timing = new TimingRecord { TotalDurationMs = 1321 },
    };

    [Fact]
    public void Produces_well_formed_html_containing_trace_metadata()
    {
        var html = HtmlReportBuilder.Build(BuildCapsule(), IncidentAnalysis.Empty);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.EndsWith("</html>", html);
        Assert.Contains("trace-abc", html);
        Assert.Contains("POST", html);
        Assert.Contains("/api/transfers", html);
        Assert.Contains("HTTP 500", html);
    }

    [Fact]
    public void Includes_a_waterfall_row_per_span_and_the_external_call_table()
    {
        var html = HtmlReportBuilder.Build(BuildCapsule(), IncidentAnalysis.Empty);

        Assert.Contains("transfer-service", html);
        Assert.Contains("ReserveBalance", html);
        Assert.Contains("balance-api", html);
        Assert.Contains("504", html);
    }

    [Fact]
    public void Includes_exceptions_and_analysis_findings()
    {
        var analysis = new IncidentAnalysis([new AnalysisFinding("Likely root cause", "payment-service timed out")]);
        var html = HtmlReportBuilder.Build(BuildCapsule(), analysis);

        Assert.Contains("TimeoutException", html);
        Assert.Contains("boom", html);
        Assert.Contains("Likely root cause", html);
        Assert.Contains("payment-service timed out", html);
    }

    [Fact]
    public void Html_encodes_untrusted_capsule_content_instead_of_emitting_it_raw()
    {
        var capsule = BuildCapsule();
        capsule.Exceptions = [new ExceptionRecord { Type = "Err", Message = "<script>alert(1)</script>", SpanId = "s1" }];
        capsule.Request = new HttpRequestRecord { Method = "POST", Path = "/x", Body = "<img src=x onerror=alert(2)>" };

        var html = HtmlReportBuilder.Build(capsule, IncidentAnalysis.Empty);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("<img src=x onerror=alert(2)>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void Handles_a_minimal_capsule_with_no_trace_or_external_calls_without_throwing()
    {
        var minimal = new Capsule { Metadata = new CapsuleMetadata { TraceId = "t", Request = new RequestInfo { Method = "GET", Path = "/" } } };
        var html = HtmlReportBuilder.Build(minimal, IncidentAnalysis.Empty);
        Assert.Contains("t", html);
    }
}
