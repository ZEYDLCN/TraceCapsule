using TraceCapsule.Core.Redaction;

namespace TraceCapsule.UnitTests;

public class RedactionEngineTests
{
    private static RedactionEngine BuildEngine() => new(new RedactionPolicy
    {
        Headers = ["Authorization", "Cookie"],
        JsonFields = ["password", "tcNumber", "cardNumber", "name"],
        Strategies =
        {
            ["cardNumber"] = RedactionStrategy.KeepLast4,
            ["tcNumber"] = RedactionStrategy.Remove,
        },
    });

    [Fact]
    public void RedactJsonBody_masks_default_strategy_field()
    {
        var engine = BuildEngine();
        var redacted = engine.RedactJsonBody("""{"name":"Zeyd"}""");
        Assert.Equal("""{"name":"Z***"}""", redacted);
    }

    [Fact]
    public void RedactJsonBody_applies_keep_last_4()
    {
        var engine = BuildEngine();
        var redacted = engine.RedactJsonBody("""{"cardNumber":"1111222233334444"}""");
        Assert.Equal("""{"cardNumber":"****4444"}""", redacted);
    }

    [Fact]
    public void RedactJsonBody_applies_remove_strategy()
    {
        var engine = BuildEngine();
        var redacted = engine.RedactJsonBody("""{"tcNumber":"12345678901"}""");
        Assert.Equal("""{"tcNumber":"***"}""", redacted);
    }

    [Fact]
    public void RedactJsonBody_recurses_into_nested_objects_and_arrays()
    {
        var engine = BuildEngine();
        var redacted = engine.RedactJsonBody("""{"user":{"name":"Zeyd"},"cards":[{"cardNumber":"1111222233334444"}]}""");
        Assert.Equal("""{"user":{"name":"Z***"},"cards":[{"cardNumber":"****4444"}]}""", redacted);
    }

    [Fact]
    public void RedactJsonBody_leaves_non_json_body_untouched()
    {
        var engine = BuildEngine();
        Assert.Equal("not json at all", engine.RedactJsonBody("not json at all"));
    }

    [Fact]
    public void RedactJsonBody_leaves_unlisted_fields_untouched()
    {
        var engine = BuildEngine();
        var redacted = engine.RedactJsonBody("""{"amount":5000}""");
        Assert.Equal("""{"amount":5000}""", redacted);
    }

    [Fact]
    public void RedactHeaders_redacts_only_configured_header_names()
    {
        var engine = BuildEngine();
        var redacted = engine.RedactHeaders(new Dictionary<string, string[]>
        {
            ["Authorization"] = ["Bearer secret-token"],
            ["X-Request-Id"] = ["abc-123"],
        });
        Assert.Equal("***", redacted["Authorization"][0]);
        Assert.Equal("abc-123", redacted["X-Request-Id"][0]);
    }
}
