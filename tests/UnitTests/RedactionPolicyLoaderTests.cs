using TraceCapsule.Core.Redaction;

namespace TraceCapsule.UnitTests;

public class RedactionPolicyLoaderTests
{
    private const string ReadmeYamlExample = """
        redaction:
          headers:
            - Authorization
            - Cookie

          json_fields:
            - password
            - token
            - tcNumber
            - cardNumber

          strategies:
            cardNumber: keep_last_4
            tcNumber: remove
        """;

    [Fact]
    public void FromYaml_parses_the_readme_example_exactly()
    {
        var policy = RedactionPolicyLoader.FromYaml(ReadmeYamlExample);

        Assert.Equal(["Authorization", "Cookie"], policy.Headers);
        Assert.Equal(["password", "token", "tcNumber", "cardNumber"], policy.JsonFields);
        Assert.Equal(RedactionStrategy.KeepLast4, policy.Strategies["cardNumber"]);
        Assert.Equal(RedactionStrategy.Remove, policy.Strategies["tcNumber"]);
    }

    [Fact]
    public void FromYaml_result_actually_redacts_like_the_readme_says_it_should()
    {
        var policy = RedactionPolicyLoader.FromYaml(ReadmeYamlExample);
        var engine = new RedactionEngine(policy);

        var redacted = engine.RedactJsonBody("""{"tcNumber":"12345678901","cardNumber":"1111222233334444"}""");

        Assert.Equal("""{"tcNumber":"***","cardNumber":"****4444"}""", redacted);
    }

    [Fact]
    public void FromYaml_throws_a_clear_error_without_a_top_level_redaction_key()
    {
        var ex = Assert.Throws<FormatException>(() => RedactionPolicyLoader.FromYaml("headers: [Authorization]"));
        Assert.Contains("redaction:", ex.Message);
    }

    [Fact]
    public void FromYaml_throws_on_an_unknown_strategy_name()
    {
        var yaml = """
            redaction:
              json_fields: [password]
              strategies:
                password: obliterate
            """;
        Assert.Throws<FormatException>(() => RedactionPolicyLoader.FromYaml(yaml));
    }

    [Fact]
    public void FromJson_deserializes_a_plain_RedactionPolicy_shape()
    {
        var json = """
            {
              "headers": ["Authorization"],
              "jsonFields": ["password"],
              "strategies": { "password": "remove" }
            }
            """;

        var policy = RedactionPolicyLoader.FromJson(json);

        Assert.Equal(["Authorization"], policy.Headers);
        Assert.Equal(["password"], policy.JsonFields);
        Assert.Equal(RedactionStrategy.Remove, policy.Strategies["password"]);
    }

    [Fact]
    public void FromFile_picks_the_format_based_on_extension()
    {
        var yamlPath = Path.Combine(Path.GetTempPath(), $"policy-{Guid.NewGuid():N}.yaml");
        var jsonPath = Path.Combine(Path.GetTempPath(), $"policy-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(yamlPath, ReadmeYamlExample);
            File.WriteAllText(jsonPath, """{"headers":["X-Api-Key"],"jsonFields":[],"strategies":{}}""");

            Assert.Equal(["Authorization", "Cookie"], RedactionPolicyLoader.FromFile(yamlPath).Headers);
            Assert.Equal(["X-Api-Key"], RedactionPolicyLoader.FromFile(jsonPath).Headers);
        }
        finally
        {
            File.Delete(yamlPath);
            File.Delete(jsonPath);
        }
    }
}
