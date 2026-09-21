using TraceCapsule.Cli.GenerateTest;
using TraceCapsule.Core.Model;

namespace TraceCapsule.ReplayTests;

public class RegressionTestGeneratorTests
{
    private static Capsule Bug(int? statusCode, string? exceptionType = null, string? requestBody = "{\"amount\":5000}", List<ExternalHttpCallRecord>? externalCalls = null) => new()
    {
        Metadata = new CapsuleMetadata { TraceId = "bug-1842" },
        Request = new HttpRequestRecord { Method = "POST", Path = "/api/transfers", QueryString = "", Body = requestBody, ContentType = "application/json" },
        Response = statusCode is null ? null : new HttpResponseRecord { StatusCode = statusCode.Value },
        Exceptions = exceptionType is null ? [] : [new ExceptionRecord { Type = exceptionType, Message = "x" }],
        ExternalHttpCalls = externalCalls ?? [],
    };

    private static Capsule Fixed(int statusCode, string? body = null) => new()
    {
        Metadata = new CapsuleMetadata { TraceId = "bug-1842-fixed" },
        Response = new HttpResponseRecord { StatusCode = statusCode, Body = body },
    };

    [Fact]
    public void Generates_a_fact_that_asserts_the_original_failure_no_longer_reproduces()
    {
        var source = RegressionTestGenerator.Generate(
            Bug(500, "TimeoutException"), Fixed(200), "My.Tests", "PaymentTimeoutTest", [], "TRACECAPSULE_TARGET_URL");

        Assert.Contains("namespace My.Tests;", source);
        Assert.Contains("public class PaymentTimeoutTest", source);
        Assert.Contains("[Fact]", source);
        Assert.Contains("Assert.NotEqual(500, (int)response.StatusCode);", source);
        Assert.Contains("Assert.Equal(200, (int)response.StatusCode);", source);
        Assert.Contains("new HttpMethod(\"POST\")", source);
        Assert.Contains("\"/api/transfers\"", source);
    }

    [Fact]
    public void Embeds_the_recorded_request_body_and_content_type()
    {
        var source = RegressionTestGenerator.Generate(Bug(500), Fixed(200), "Ns", "T", [], "URL");
        Assert.Contains("MediaTypeHeaderValue.Parse(\"application/json\")", source);
        Assert.Contains("new StringContent(\"{\\\"amount\\\":5000}\", encoding)", source);
        Assert.Contains("request.Content.Headers.ContentType = contentType;", source);
    }

    [Fact]
    public void Omits_the_content_assignment_when_the_recorded_request_has_no_body()
    {
        var source = RegressionTestGenerator.Generate(Bug(500, requestBody: null), Fixed(200), "Ns", "T", [], "URL");
        Assert.DoesNotContain("request.Content", source);
    }

    [Fact]
    public void Asserts_a_structural_body_diff_against_the_fixed_capsules_response()
    {
        var source = RegressionTestGenerator.Generate(
            Bug(500), Fixed(200, """{"status":"completed"}"""), "Ns", "T", ["$.completedAt"], "URL");

        Assert.Contains("JsonBodyDiff.Compare(", source);
        Assert.Contains("expectedJson: \"{\\\"status\\\":\\\"completed\\\"}\",", source);
        Assert.Contains("ignoredPaths: [\"$.completedAt\"]", source);
        Assert.Contains("Assert.Empty(differences);", source);
    }

    [Fact]
    public void Omits_the_body_diff_block_when_the_fixed_capsule_has_no_body()
    {
        var source = RegressionTestGenerator.Generate(Bug(500), Fixed(200), "Ns", "T", [], "URL");
        Assert.DoesNotContain("JsonBodyDiff", source);
    }

    [Fact]
    public void Describes_a_no_response_original_failure_without_asserting_a_status_code()
    {
        var source = RegressionTestGenerator.Generate(Bug(null, "TaskCanceledException"), Fixed(200), "Ns", "T", [], "URL");
        Assert.Contains("Production originally failed with no response (TaskCanceledException)", source);
        Assert.DoesNotContain("Assert.NotEqual(", source);
    }

    [Fact]
    public void Lists_recorded_external_dependencies_in_a_header_comment()
    {
        var source = RegressionTestGenerator.Generate(
            Bug(500, externalCalls: [new ExternalHttpCallRecord { DependencyName = "fraud-api" }]),
            Fixed(200), "Ns", "T", [], "URL");
        Assert.Contains("fraud-api", source);
        Assert.Contains("AddTraceCapsuleReplayEnvironment", source);
    }

    [Fact]
    public void Reads_the_target_url_from_the_given_environment_variable_name()
    {
        var source = RegressionTestGenerator.Generate(Bug(500), Fixed(200), "Ns", "T", [], "MY_TARGET_URL");
        Assert.Contains("Environment.GetEnvironmentVariable(\"MY_TARGET_URL\")", source);
    }

    [Fact]
    public void Throws_when_the_bug_capsule_has_no_recorded_request()
    {
        var bug = Bug(500);
        bug.Request = null;
        Assert.Throws<InvalidOperationException>(() => RegressionTestGenerator.Generate(bug, Fixed(200), "Ns", "T", [], "URL"));
    }

    [Fact]
    public void Throws_when_the_fixed_capsule_has_no_recorded_response()
    {
        var fixedCapsule = Fixed(200);
        fixedCapsule.Response = null;
        Assert.Throws<InvalidOperationException>(() => RegressionTestGenerator.Generate(Bug(500), fixedCapsule, "Ns", "T", [], "URL"));
    }

    [Theory]
    [InlineData("bug 1842!", "bug_1842_")]
    [InlineData("7f92bd", "_7f92bd")]
    [InlineData("102-abc", "_102_abc")]
    public void SanitizeIdentifier_replaces_non_alphanumerics_and_never_starts_with_a_digit(string input, string expected) =>
        Assert.Equal(expected, RegressionTestGenerator.SanitizeIdentifier(input));
}
