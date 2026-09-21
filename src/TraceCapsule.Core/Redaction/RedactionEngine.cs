using System.Text.Json.Nodes;

namespace TraceCapsule.Core.Redaction;

/// <summary>Applies a <see cref="RedactionPolicy"/> to headers and JSON bodies before
/// anything is written into a capsule. This runs on every capture path (HTTP request,
/// HTTP response, external calls, queue payloads) — nothing unredacted should ever touch
/// disk.</summary>
public sealed class RedactionEngine(RedactionPolicy policy)
{
    private readonly RedactionPolicy _policy = policy;

    public static readonly RedactionEngine Default = new(RedactionPolicy.Default());

    public Dictionary<string, string[]> RedactHeaders(IEnumerable<KeyValuePair<string, string[]>> headers)
    {
        var result = new Dictionary<string, string[]>();
        foreach (var (name, values) in headers)
        {
            result[name] = _policy.RedactsHeader(name) ? [Apply(RedactionStrategy.Remove, values.Length > 0 ? values[0] : "")] : values;
        }
        return result;
    }

    /// <summary>Redacts a JSON document's field values in place, at any nesting depth.
    /// Returns the input unchanged (including <c>null</c>/empty) if it isn't valid JSON —
    /// bodies are not assumed to always be JSON.</summary>
    public string? RedactJsonBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch { return body; }
        if (node is null) return body;
        RedactNode(node);
        return node.ToJsonString();
    }

    private void RedactNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[key];
                    if (child is null) continue;
                    if (_policy.RedactsField(key))
                    {
                        var original = child is JsonValue value && value.TryGetValue(out string? s) ? s : child.ToJsonString();
                        obj[key] = Apply(_policy.StrategyFor(key), original ?? "");
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        RedactNode(child);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null) RedactNode(item);
                }
                break;
        }
    }

    private static string Apply(RedactionStrategy strategy, string value) => strategy switch
    {
        RedactionStrategy.KeepLast4 => value.Length <= 4 ? "****" : "****" + value[^4..],
        RedactionStrategy.Remove => "***",
        _ => MaskDefault(value),
    };

    /// <summary>Default mask: first letter + asterisks, matching the README's
    /// <c>"Zeyd" -&gt; "Z***"</c> example. Falls back to a fixed placeholder for anything
    /// too short to partially reveal.</summary>
    private static string MaskDefault(string value) =>
        value.Length <= 1 ? "***" : value[..1] + new string('*', Math.Min(value.Length - 1, 3));
}
