using TraceCapsule.Core.Analysis;
using TraceCapsule.Core.Model;

namespace TraceCapsule.UnitTests;

public class HeuristicIncidentAnalyzerTests
{
    [Fact]
    public void Reports_no_findings_for_a_clean_capsule()
    {
        var analyzer = new HeuristicIncidentAnalyzer();
        var result = analyzer.Analyze(new Capsule { Metadata = new CapsuleMetadata { TraceId = "t" } });
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Names_the_failing_span_when_an_exception_was_recorded()
    {
        var analyzer = new HeuristicIncidentAnalyzer();
        var capsule = new Capsule
        {
            Metadata = new CapsuleMetadata { TraceId = "t" },
            Trace = [new SpanRecord { SpanId = "s1", Name = "PaymentService.ReserveBalance", ServiceName = "payment-service" }],
            Exceptions = [new ExceptionRecord { Type = "TimeoutException", Message = "timed out", SpanId = "s1" }],
        };

        var result = analyzer.Analyze(capsule);

        var rootCause = Assert.Single(result.Findings, f => f.Title == "Likely root cause");
        Assert.Contains("PaymentService.ReserveBalance", rootCause.Detail);
        Assert.Contains("TimeoutException", rootCause.Detail);
    }

    [Fact]
    public void Flags_the_slowest_failing_external_call()
    {
        var analyzer = new HeuristicIncidentAnalyzer();
        var capsule = new Capsule
        {
            Metadata = new CapsuleMetadata { TraceId = "t" },
            ExternalHttpCalls =
            [
                new ExternalHttpCallRecord { DependencyName = "fraud-api", Url = "https://fraud", ResponseStatusCode = 200, DurationMs = 50 },
                new ExternalHttpCallRecord { DependencyName = "balance-api", Url = "https://balance", ResponseStatusCode = 504, DurationMs = 2400 },
            ],
        };

        var result = analyzer.Analyze(capsule);

        var finding = Assert.Single(result.Findings);
        Assert.Contains("balance-api", finding.Detail);
        Assert.Contains("504", finding.Detail);
    }

    [Fact]
    public void Flags_a_published_message_that_was_never_consumed()
    {
        var analyzer = new HeuristicIncidentAnalyzer();
        var capsule = new Capsule
        {
            Metadata = new CapsuleMetadata { TraceId = "t" },
            Events =
            [
                new QueueEventRecord { EventType = "TransferRequested", MessageId = "m1", RoutingKey = "transfers", Direction = QueueEventDirection.Publish },
            ],
        };

        var result = analyzer.Analyze(capsule);

        var finding = Assert.Single(result.Findings, f => f.Title == "Message not consumed");
        Assert.Contains("m1", finding.Detail);
    }
}
