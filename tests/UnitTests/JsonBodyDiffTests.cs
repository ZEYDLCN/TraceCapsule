using TraceCapsule.Core.Comparison;

namespace TraceCapsule.UnitTests;

public class JsonBodyDiffTests
{
    [Theory]
    [InlineData("{\"name\":\"a\"}", "{\"name\":\"\\u0061\"}")]
    [InlineData("{\"name\":\"İstanbul\"}", "{\"name\":\"\\u0130stanbul\"}")]
    [InlineData("[\"🌍\"]", "[\"\\ud83c\\udf0d\"]")]
    [InlineData("{\"url\":\"a/b\"}", "{\"url\":\"a\\/b\"}")]
    public void Equivalent_string_encodings_are_equal(string expected, string actual) =>
        Assert.Empty(JsonBodyDiff.Compare(expected, actual));

    [Fact]
    public void Different_decoded_strings_still_report_their_values()
    {
        var difference = Assert.Single(JsonBodyDiff.Compare("{\"name\":\"\\u0061\"}", "{\"name\":\"b\"}"));
        Assert.Equal("$.name", difference.Path);
        Assert.Equal("a", difference.Expected);
        Assert.Equal("b", difference.Actual);
    }

    [Fact]
    public void Reports_no_differences_for_identical_json()
    {
        var differences = JsonBodyDiff.Compare("""{"a":1,"b":"x"}""", """{"a":1,"b":"x"}""");
        Assert.Empty(differences);
    }

    [Fact]
    public void Reports_no_differences_when_both_bodies_are_null()
    {
        Assert.Empty(JsonBodyDiff.Compare(null, null));
    }

    [Fact]
    public void Reports_a_scalar_value_mismatch_by_path()
    {
        var differences = JsonBodyDiff.Compare("""{"riskScore":81}""", """{"riskScore":42}""");
        var diff = Assert.Single(differences);
        Assert.Equal("$.riskScore", diff.Path);
        Assert.Equal("81", diff.Expected);
        Assert.Equal("42", diff.Actual);
    }

    [Fact]
    public void Ignores_a_named_path_even_when_it_differs()
    {
        var differences = JsonBodyDiff.Compare(
            """{"status":"ok","createdAt":"2026-01-01T00:00:00Z"}""",
            """{"status":"ok","createdAt":"2026-09-21T10:00:00Z"}""",
            ["$.createdAt"]);
        Assert.Empty(differences);
    }

    [Fact]
    public void Ignores_a_wildcard_path_across_every_array_element()
    {
        var differences = JsonBodyDiff.Compare(
            """{"items":[{"id":1,"updatedAt":"a"},{"id":2,"updatedAt":"b"}]}""",
            """{"items":[{"id":1,"updatedAt":"z"},{"id":2,"updatedAt":"y"}]}""",
            ["$.items[*].updatedAt"]);
        Assert.Empty(differences);
    }

    [Fact]
    public void Reports_a_missing_property_on_one_side()
    {
        var differences = JsonBodyDiff.Compare("""{"a":1,"b":2}""", """{"a":1}""");
        var diff = Assert.Single(differences);
        Assert.Equal("$.b", diff.Path);
        Assert.Equal("2", diff.Expected);
        Assert.Null(diff.Actual);
    }

    [Fact]
    public void Reports_a_type_mismatch_as_a_difference()
    {
        var differences = JsonBodyDiff.Compare("""{"a":1}""", """{"a":"1"}""");
        Assert.Single(differences, d => d.Path == "$.a");
    }

    [Fact]
    public void Reports_a_difference_in_array_length()
    {
        var differences = JsonBodyDiff.Compare("""{"items":[1,2]}""", """{"items":[1]}""");
        var diff = Assert.Single(differences);
        Assert.Equal("$.items[1]", diff.Path);
    }

    [Fact]
    public void Falls_back_to_a_raw_string_compare_when_a_side_is_not_valid_json()
    {
        var differences = JsonBodyDiff.Compare("not json", "also not json");
        var diff = Assert.Single(differences);
        Assert.Equal("$", diff.Path);
        Assert.Equal("not json", diff.Expected);
        Assert.Equal("also not json", diff.Actual);
    }

    [Fact]
    public void Reports_no_difference_when_one_side_is_entirely_missing_and_that_path_is_ignored()
    {
        Assert.Empty(JsonBodyDiff.Compare(null, """{"a":1}""", ["$"]));
    }
}
