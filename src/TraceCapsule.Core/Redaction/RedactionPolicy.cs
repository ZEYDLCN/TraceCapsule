namespace TraceCapsule.Core.Redaction;

public enum RedactionStrategy
{
    /// <summary>Replace the whole value with a fixed mask (default).</summary>
    Mask,

    /// <summary>Drop the field/header entirely.</summary>
    Remove,

    /// <summary>Keep only the last 4 characters, mask the rest (card numbers, etc).</summary>
    KeepLast4,
}

/// <summary>Declarative redaction policy — mirrors the YAML shape shown in the README:
/// <code>
/// redaction:
///   headers: [Authorization, Cookie]
///   json_fields: [password, token, tcNumber, cardNumber]
///   strategies:
///     cardNumber: keep_last_4
///     tcNumber: remove
/// </code>
/// Fields listed in <see cref="JsonFields"/> without an explicit entry in
/// <see cref="Strategies"/> fall back to <see cref="RedactionStrategy.Mask"/>.</summary>
public sealed class RedactionPolicy
{
    public List<string> Headers { get; set; } = new();
    public List<string> JsonFields { get; set; } = new();
    public Dictionary<string, RedactionStrategy> Strategies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static RedactionPolicy Default() => new()
    {
        Headers = ["Authorization", "Cookie", "Set-Cookie"],
        JsonFields = ["password", "token", "secret"],
    };

    public RedactionStrategy StrategyFor(string fieldName) =>
        Strategies.TryGetValue(fieldName, out var strategy) ? strategy : RedactionStrategy.Mask;

    public bool RedactsHeader(string headerName) =>
        Headers.Any(h => string.Equals(h, headerName, StringComparison.OrdinalIgnoreCase));

    public bool RedactsField(string fieldName) =>
        JsonFields.Any(f => string.Equals(f, fieldName, StringComparison.OrdinalIgnoreCase));
}
