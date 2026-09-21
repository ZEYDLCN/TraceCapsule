using System.Text.Json;
using System.Text.RegularExpressions;

namespace TraceCapsule.Core.Comparison;

/// <summary>One JSON path where two bodies disagree (see <see cref="JsonBodyDiff.Compare"/>).
/// <see cref="Expected"/>/<see cref="Actual"/> are <c>null</c> when the path is missing on
/// that side entirely (as opposed to present with a JSON <c>null</c> value).</summary>
public sealed record JsonDifference(string Path, string? Expected, string? Actual);

/// <summary>Structural JSON diffing for response bodies, so <c>tracecapsule compare</c> and
/// generated regression tests can assert on a response's meaning rather than its exact bytes.
/// A byte-for-byte comparison would flag every replay as a mismatch purely from fields that
/// are *expected* to vary between runs (timestamps, generated ids, durations) — this instead
/// walks both documents together and reports only the paths that actually disagree, with
/// <c>ignoredPaths</c> (see <see cref="Compare"/>) letting the caller name the volatile ones
/// up front (e.g. <c>"$.createdAt"</c>, or <c>"$.items[*].id"</c> for every element of an
/// array).</summary>
public static class JsonBodyDiff
{
    /// <summary>Compares two JSON documents and returns every path whose value differs,
    /// skipping any path matched by <paramref name="ignoredPaths"/>. Falls back to a single
    /// <c>"$"</c>-path diff when either side isn't valid JSON (a plain string mismatch is
    /// still useful to report, just not structurally).</summary>
    public static IReadOnlyList<JsonDifference> Compare(string? expectedJson, string? actualJson, IEnumerable<string>? ignoredPaths = null)
    {
        var ignored = (ignoredPaths ?? []).ToArray();
        if (IsBlank(expectedJson) && IsBlank(actualJson)) return [];
        if (IsBlank(expectedJson) || IsBlank(actualJson))
            return IsPathIgnored("$", ignored) ? [] : [new JsonDifference("$", expectedJson, actualJson)];

        JsonDocument? expectedDoc = null;
        JsonDocument? actualDoc = null;
        try
        {
            expectedDoc = JsonDocument.Parse(expectedJson!);
            actualDoc = JsonDocument.Parse(actualJson!);
            var differences = new List<JsonDifference>();
            Diff(expectedDoc.RootElement, actualDoc.RootElement, "$", ignored, differences);
            return differences;
        }
        catch (JsonException)
        {
            // Not JSON on at least one side — a raw string compare is the best we can do.
            return expectedJson == actualJson || IsPathIgnored("$", ignored)
                ? []
                : [new JsonDifference("$", expectedJson, actualJson)];
        }
        finally
        {
            expectedDoc?.Dispose();
            actualDoc?.Dispose();
        }
    }

    private static bool IsBlank(string? json) => string.IsNullOrWhiteSpace(json);

    private static void Diff(JsonElement? expected, JsonElement? actual, string path, string[] ignoredPaths, List<JsonDifference> differences)
    {
        if (IsPathIgnored(path, ignoredPaths)) return;

        if (expected is null || actual is null)
        {
            if (expected is null && actual is null) return;
            differences.Add(new JsonDifference(path, Describe(expected), Describe(actual)));
            return;
        }

        var e = expected.Value;
        var a = actual.Value;
        if (e.ValueKind != a.ValueKind)
        {
            differences.Add(new JsonDifference(path, Describe(e), Describe(a)));
            return;
        }

        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var names = e.EnumerateObject().Select(p => p.Name)
                    .Concat(a.EnumerateObject().Select(p => p.Name))
                    .Distinct(StringComparer.Ordinal);
                foreach (var name in names)
                {
                    var childPath = $"{path}.{name}";
                    var childExpected = e.TryGetProperty(name, out var ev) ? ev : (JsonElement?)null;
                    var childActual = a.TryGetProperty(name, out var av) ? av : (JsonElement?)null;
                    Diff(childExpected, childActual, childPath, ignoredPaths, differences);
                }
                break;

            case JsonValueKind.Array:
                var length = Math.Max(e.GetArrayLength(), a.GetArrayLength());
                for (var i = 0; i < length; i++)
                {
                    var childPath = $"{path}[{i}]";
                    var childExpected = i < e.GetArrayLength() ? e[i] : (JsonElement?)null;
                    var childActual = i < a.GetArrayLength() ? a[i] : (JsonElement?)null;
                    Diff(childExpected, childActual, childPath, ignoredPaths, differences);
                }
                break;

            case JsonValueKind.String:
                if (!string.Equals(e.GetString(), a.GetString(), StringComparison.Ordinal))
                    differences.Add(new JsonDifference(path, Describe(e), Describe(a)));
                break;

            default:
                if (!string.Equals(e.GetRawText(), a.GetRawText(), StringComparison.Ordinal))
                    differences.Add(new JsonDifference(path, Describe(e), Describe(a)));
                break;
        }
    }

    private static string? Describe(JsonElement? element) => element switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } e => e.GetString(),
        var e => e.Value.GetRawText(),
    };

    /// <summary>A path matches an ignore pattern either verbatim, or via <c>*</c> as a
    /// wildcard for one path segment/array index — e.g. <c>"$.items[*].updatedAt"</c> ignores
    /// that field on every array element.</summary>
    private static bool IsPathIgnored(string path, string[] ignoredPaths)
    {
        foreach (var pattern in ignoredPaths)
        {
            if (string.Equals(pattern, path, StringComparison.Ordinal)) return true;
            if (pattern.Contains('*') && Regex.IsMatch(path, WildcardToRegex(pattern))) return true;
        }
        return false;
    }

    private static string WildcardToRegex(string pattern) =>
        "^" + string.Join(".*", Regex.Escape(pattern).Split("\\*")) + "$";
}
