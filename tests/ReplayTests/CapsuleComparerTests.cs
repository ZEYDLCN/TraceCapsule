using TraceCapsule.Cli.Compare;
using TraceCapsule.Core.Model;

namespace TraceCapsule.ReplayTests;

public class CapsuleComparerTests
{
    private static Capsule Build(int? statusCode, string? exceptionType = null, string spanStatus = "Ok") => new()
    {
        Metadata = new CapsuleMetadata { TraceId = "t" },
        Response = statusCode is null ? null : new HttpResponseRecord { StatusCode = statusCode.Value },
        Exceptions = exceptionType is null ? [] : [new ExceptionRecord { Type = exceptionType, Message = "x" }],
        Trace = [new SpanRecord { SpanId = "s1", Name = "PaymentService.ReserveBalance", Status = spanStatus }],
    };

    [Fact]
    public void Reports_a_match_when_status_exception_and_span_status_all_agree()
    {
        var report = CapsuleComparer.Compare(Build(500, "TimeoutException", "Error"), Build(500, "TimeoutException", "Error"));
        Assert.True(report.AllMatch);
        Assert.All(report.Lines, l => Assert.True(l.IsMatch));
    }

    [Fact]
    public void Reports_a_mismatch_on_status_code()
    {
        var report = CapsuleComparer.Compare(Build(500), Build(200));
        Assert.False(report.AllMatch);
        var statusLine = Assert.Single(report.Lines, l => l.Label == "HTTP status");
        Assert.False(statusLine.IsMatch);
    }

    [Fact]
    public void Reports_a_mismatch_when_only_one_side_has_an_exception()
    {
        var report = CapsuleComparer.Compare(Build(500, "TimeoutException"), Build(500));
        var exceptionLine = Assert.Single(report.Lines, l => l.Label == "Exception");
        Assert.False(exceptionLine.IsMatch);
        Assert.Equal("(none)", exceptionLine.ReplayValue);
    }

    [Fact]
    public void Compares_spans_present_on_both_sides_by_name()
    {
        var report = CapsuleComparer.Compare(Build(200, spanStatus: "Error"), Build(200, spanStatus: "Ok"));
        var spanLine = Assert.Single(report.Lines, l => l.Label.Contains("PaymentService.ReserveBalance"));
        Assert.False(spanLine.IsMatch);
    }
}
