using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using LeanKernel.Tools.BuiltIn.Common;

namespace LeanKernel.Tests.Unit.Tools;

public class ToolArgumentReaderTests
{
    private static Dictionary<string, object?> Args(params (string key, object? value)[] entries) =>
        entries.ToDictionary(e => e.key, e => e.value);

    private static JsonElement JsonElement(object value) =>
        JsonSerializer.SerializeToElement(value);

    // ==================== GetString ====================

    [Fact]
    public void GetString_returns_empty_when_key_is_missing()
    {
        ToolArgumentReader.GetString(new Dictionary<string, object?>(), "key").Should().BeEmpty();
    }

    [Fact]
    public void GetString_returns_empty_when_value_is_null()
    {
        ToolArgumentReader.GetString(Args(("key", null)), "key").Should().BeEmpty();
    }

    [Fact]
    public void GetString_returns_string_value()
    {
        ToolArgumentReader.GetString(Args(("key", "hello")), "key").Should().Be("hello");
    }

    [Fact]
    public void GetString_returns_empty_when_json_element_is_null()
    {
        var args = Args(("key", JsonSerializer.SerializeToElement<string?>(null)));
        ToolArgumentReader.GetString(args, "key").Should().BeEmpty();
    }

    [Fact]
    public void GetString_returns_string_from_json_element_string()
    {
        ToolArgumentReader.GetString(Args(("key", JsonElement("hello"))), "key").Should().Be("hello");
    }

    [Fact]
    public void GetString_returns_string_from_json_element_number()
    {
        ToolArgumentReader.GetString(Args(("key", JsonElement(42))), "key").Should().Be("42");
    }

    [Fact]
    public void GetString_returns_string_from_non_string_value()
    {
        ToolArgumentReader.GetString(Args(("key", 123)), "key").Should().Be("123");
    }

    [Fact]
    public void GetString_throws_on_null_arguments()
    {
        Action act = () => ToolArgumentReader.GetString(null!, "key");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetString_throws_on_null_or_whitespace_name()
    {
        var args = new Dictionary<string, object?>();
        Action nullName = () => ToolArgumentReader.GetString(args, null!);
        nullName.Should().Throw<ArgumentException>();
        Action emptyName = () => ToolArgumentReader.GetString(args, "");
        emptyName.Should().Throw<ArgumentException>();
        Action whitespaceName = () => ToolArgumentReader.GetString(args, "  ");
        whitespaceName.Should().Throw<ArgumentException>();
    }

    // ==================== GetInt32OrDefault ====================

    [Fact]
    public void GetInt32OrDefault_returns_default_when_key_is_missing()
    {
        ToolArgumentReader.GetInt32OrDefault(new Dictionary<string, object?>(), "key", 42).Should().Be(42);
    }

    [Fact]
    public void GetInt32OrDefault_returns_default_when_value_is_null()
    {
        ToolArgumentReader.GetInt32OrDefault(Args(("key", null)), "key", 42).Should().Be(42);
    }

    [Fact]
    public void GetInt32OrDefault_returns_int_value()
    {
        ToolArgumentReader.GetInt32OrDefault(Args(("key", 42)), "key", -1).Should().Be(42);
    }

    [Fact]
    public void GetInt32OrDefault_returns_long_value_within_range()
    {
        ToolArgumentReader.GetInt32OrDefault(Args(("key", 42L)), "key", -1).Should().Be(42);
    }

    [Fact]
    public void GetInt32OrDefault_returns_default_when_long_overflows()
    {
        ToolArgumentReader.GetInt32OrDefault(Args(("key", (long)int.MaxValue + 1)), "key", -1).Should().Be(-1);
    }

    [Fact]
    public void GetInt32OrDefault_parses_json_element_number()
    {
        ToolArgumentReader.GetInt32OrDefault(Args(("key", JsonElement(42))), "key", -1).Should().Be(42);
    }

    [Fact]
    public void GetInt32OrDefault_parses_json_element_string_number()
    {
        ToolArgumentReader.GetInt32OrDefault(Args(("key", JsonElement("42"))), "key", -1).Should().Be(42);
    }

    [Fact]
    public void GetInt32OrDefault_parses_numeric_string()
    {
        ToolArgumentReader.GetInt32OrDefault(Args(("key", "42")), "key", -1).Should().Be(42);
    }

    [Fact]
    public void GetInt32OrDefault_returns_default_for_invalid_string()
    {
        ToolArgumentReader.GetInt32OrDefault(Args(("key", "not_a_number")), "key", -1).Should().Be(-1);
    }

    [Fact]
    public void GetInt32OrDefault_converts_other_numeric_types()
    {
        ToolArgumentReader.GetInt32OrDefault(Args(("key", (short)42)), "key", -1).Should().Be(42);
    }

    // ==================== GetBoolOrDefault ====================

    [Fact]
    public void GetBoolOrDefault_returns_default_when_key_is_missing()
    {
        ToolArgumentReader.GetBoolOrDefault(new Dictionary<string, object?>(), "key", true).Should().BeTrue();
    }

    [Fact]
    public void GetBoolOrDefault_returns_default_when_value_is_null()
    {
        ToolArgumentReader.GetBoolOrDefault(Args(("key", null)), "key", true).Should().BeTrue();
    }

    [Fact]
    public void GetBoolOrDefault_returns_true()
    {
        ToolArgumentReader.GetBoolOrDefault(Args(("key", true)), "key", false).Should().BeTrue();
    }

    [Fact]
    public void GetBoolOrDefault_returns_false()
    {
        ToolArgumentReader.GetBoolOrDefault(Args(("key", false)), "key", true).Should().BeFalse();
    }

    [Fact]
    public void GetBoolOrDefault_returns_true_from_json_element()
    {
        ToolArgumentReader.GetBoolOrDefault(Args(("key", JsonElement(true))), "key", false).Should().BeTrue();
    }

    [Fact]
    public void GetBoolOrDefault_returns_false_from_json_element()
    {
        ToolArgumentReader.GetBoolOrDefault(Args(("key", JsonElement(false))), "key", true).Should().BeFalse();
    }

    [Fact]
    public void GetBoolOrDefault_parses_true_string()
    {
        ToolArgumentReader.GetBoolOrDefault(Args(("key", "true")), "key", false).Should().BeTrue();
    }

    [Fact]
    public void GetBoolOrDefault_parses_false_string()
    {
        ToolArgumentReader.GetBoolOrDefault(Args(("key", "false")), "key", true).Should().BeFalse();
    }

    [Fact]
    public void GetBoolOrDefault_returns_default_for_invalid_string()
    {
        ToolArgumentReader.GetBoolOrDefault(Args(("key", "not_a_bool")), "key", true).Should().BeTrue();
    }

    // ==================== GetJsonObjectOrNull ====================

    [Fact]
    public void GetJsonObjectOrNull_returns_null_when_key_is_missing()
    {
        ToolArgumentReader.GetJsonObjectOrNull(new Dictionary<string, object?>(), "key").Should().BeNull();
    }

    [Fact]
    public void GetJsonObjectOrNull_returns_null_when_value_is_null()
    {
        ToolArgumentReader.GetJsonObjectOrNull(Args(("key", null)), "key").Should().BeNull();
    }

    [Fact]
    public void GetJsonObjectOrNull_returns_json_object()
    {
        var obj = new JsonObject { ["a"] = 1 };
        ToolArgumentReader.GetJsonObjectOrNull(Args(("key", obj)), "key").Should().BeSameAs(obj);
    }

    [Fact]
    public void GetJsonObjectOrNull_returns_null_for_non_object_json_node()
    {
        ToolArgumentReader.GetJsonObjectOrNull(Args(("key", JsonValue.Create(42)!)), "key").Should().BeNull();
    }

    [Fact]
    public void GetJsonObjectOrNull_converts_json_element_object()
    {
        var result = ToolArgumentReader.GetJsonObjectOrNull(Args(("key", JsonElement(new { a = 1 }))), "key");
        result.Should().NotBeNull();
        result!["a"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void GetJsonObjectOrNull_converts_object_dictionary()
    {
        var result = ToolArgumentReader.GetJsonObjectOrNull(
            Args(("key", new Dictionary<string, object?> { ["a"] = 1 })), "key");
        result.Should().NotBeNull();
        result!["a"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void GetJsonObjectOrNull_converts_string_dictionary()
    {
        var result = ToolArgumentReader.GetJsonObjectOrNull(
            Args(("key", new Dictionary<string, string> { ["a"] = "1" })), "key");
        result.Should().NotBeNull();
        result!["a"]!.GetValue<string>().Should().Be("1");
    }

    // ==================== GetStringDictionary ====================

    [Fact]
    public void GetStringDictionary_returns_empty_when_key_is_missing()
    {
        ToolArgumentReader.GetStringDictionary(new Dictionary<string, object?>(), "key").Should().BeEmpty();
    }

    [Fact]
    public void GetStringDictionary_returns_empty_when_value_is_null()
    {
        ToolArgumentReader.GetStringDictionary(Args(("key", null)), "key").Should().BeEmpty();
    }

    [Fact]
    public void GetStringDictionary_returns_string_dictionary()
    {
        var result = ToolArgumentReader.GetStringDictionary(
            Args(("key", new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" })), "key");
        result.Should().Equal(new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" });
    }

    [Fact]
    public void GetStringDictionary_converts_object_dictionary()
    {
        var result = ToolArgumentReader.GetStringDictionary(
            Args(("key", new Dictionary<string, object?> { ["A"] = 1, ["B"] = "hello" })), "key");
        result.Should().Equal(new Dictionary<string, string> { ["A"] = "1", ["B"] = "hello" });
    }

    [Fact]
    public void GetStringDictionary_converts_json_object()
    {
        var jsonObj = new JsonObject
        {
            ["A"] = JsonValue.Create(1),
            ["B"] = JsonValue.Create("hello")
        };
        var result = ToolArgumentReader.GetStringDictionary(Args(("key", jsonObj)), "key");
        result.Should().Equal(new Dictionary<string, string> { ["A"] = "1", ["B"] = "hello" });
    }

    [Fact]
    public void GetStringDictionary_converts_json_element_object()
    {
        var result = ToolArgumentReader.GetStringDictionary(
            Args(("key", JsonElement(new { A = 1, B = "hello" }))), "key");
        result.Should().Equal(new Dictionary<string, string> { ["A"] = "1", ["B"] = "hello" });
    }

    [Fact]
    public void GetStringDictionary_uses_ordinal_ignore_case()
    {
        var result = ToolArgumentReader.GetStringDictionary(
            Args(("key", new Dictionary<string, string> { ["A"] = "1" })), "key");
        result.ContainsKey("a").Should().BeTrue();
        result["a"].Should().Be("1");
    }

    // ==================== GetObjectDictionary ====================

    [Fact]
    public void GetObjectDictionary_returns_empty_when_key_is_missing()
    {
        ToolArgumentReader.GetObjectDictionary(new Dictionary<string, object?>(), "key").Should().BeEmpty();
    }

    [Fact]
    public void GetObjectDictionary_returns_empty_when_value_is_null()
    {
        ToolArgumentReader.GetObjectDictionary(Args(("key", null)), "key").Should().BeEmpty();
    }

    [Fact]
    public void GetObjectDictionary_returns_dictionary()
    {
        var dict = new Dictionary<string, object?> { ["A"] = 1, ["B"] = "hello" };
        var result = ToolArgumentReader.GetObjectDictionary(Args(("key", dict)), "key");
        result.Should().Equal(dict);
    }

    [Fact]
    public void GetObjectDictionary_converts_read_only_dictionary()
    {
        var inner = new Dictionary<string, object?> { ["A"] = 1 };
        var readOnly = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(inner);
        var result = ToolArgumentReader.GetObjectDictionary(Args(("key", readOnly)), "key");
        result.Should().Equal(inner);
    }

    [Fact]
    public void GetObjectDictionary_converts_json_element_object()
    {
        var result = ToolArgumentReader.GetObjectDictionary(
            Args(("key", JsonElement(new { A = 1, B = "hello" }))), "key");
        result.Should().Equal(new Dictionary<string, object?> { ["A"] = 1, ["B"] = "hello" });
    }

    [Fact]
    public void GetObjectDictionary_throws_for_non_object_value()
    {
        Action act = () => ToolArgumentReader.GetObjectDictionary(Args(("key", "not_a_dict")), "key");
        act.Should().Throw<ArgumentException>().WithMessage("*must be an object map*");
    }
}
