using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TraceCapsule.Core.Redaction;

/// <summary>Loads a <see cref="RedactionPolicy"/> from a config file instead of only from
/// code — the README shows the policy as YAML:
/// <code>
/// redaction:
///   headers: [Authorization, Cookie]
///   json_fields: [password, token, tcNumber, cardNumber]
///   strategies:
///     cardNumber: keep_last_4
///     tcNumber: remove
/// </code>
/// A plain JSON file works too, as a direct serialization of <see cref="RedactionPolicy"/>
/// (camelCase, no wrapping "redaction" key needed):
/// <code>
/// { "headers": ["Authorization"], "jsonFields": ["password"], "strategies": { "password": "remove" } }
/// </code>
/// </summary>
public static class RedactionPolicyLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static RedactionPolicy FromFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var content = File.ReadAllText(path);
        return extension switch
        {
            ".yaml" or ".yml" => FromYaml(content),
            _ => FromJson(content),
        };
    }

    public static RedactionPolicy FromJson(string json) =>
        JsonSerializer.Deserialize<RedactionPolicy>(json, JsonOptions)
        ?? throw new FormatException("Redaction policy JSON deserialized to null.");

    public static RedactionPolicy FromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var root = deserializer.Deserialize<YamlRoot>(yaml);
        var section = root?.Redaction ?? throw new FormatException(
            "Expected a top-level 'redaction:' key (see the README's redaction policy example).");

        var policy = new RedactionPolicy
        {
            Headers = section.Headers ?? [],
            JsonFields = section.JsonFields ?? [],
        };
        if (section.Strategies is not null)
        {
            foreach (var (field, strategyName) in section.Strategies)
            {
                policy.Strategies[field] = ParseStrategy(strategyName);
            }
        }
        return policy;
    }

    private static RedactionStrategy ParseStrategy(string value) => value.Trim().ToLowerInvariant() switch
    {
        "remove" => RedactionStrategy.Remove,
        "keep_last_4" or "keeplast4" => RedactionStrategy.KeepLast4,
        "mask" => RedactionStrategy.Mask,
        _ => throw new FormatException($"Unknown redaction strategy '{value}'. Expected one of: mask, remove, keep_last_4."),
    };

    private sealed class YamlRoot
    {
        public YamlRedactionSection? Redaction { get; set; }
    }

    private sealed class YamlRedactionSection
    {
        public List<string>? Headers { get; set; }
        public List<string>? JsonFields { get; set; }
        public Dictionary<string, string>? Strategies { get; set; }
    }
}
