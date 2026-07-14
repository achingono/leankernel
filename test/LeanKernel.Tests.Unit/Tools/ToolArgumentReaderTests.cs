using System.Text.Json;
using FluentAssertions;
using LeanKernel.Logic.Tools;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class ToolArgumentReaderTests
{
    [Fact]
    public void GetString_ReturnsStringValue()
    {
        var args = new Dictionary<string, object?> { ["q"] = "hello" };
        ToolArgumentReader.GetString(args, "q").Should().Be("hello");
    }

    [Fact]
    public void GetString_MissingKey_ReturnsNull()
    {
        var args = new Dictionary<string, object?>();
        ToolArgumentReader.GetString(args, "missing").Should().BeNull();
    }

    [Fact]
    public void GetString_NullValue_ReturnsNull()
    {
        var args = new Dictionary<string, object?> { ["q"] = null };
        ToolArgumentReader.GetString(args, "q").Should().BeNull();
    }

    [Fact]
    public void GetString_FromJsonElement_ReturnsString()
    {
        using var doc = JsonDocument.Parse("\"json_string\"");
        var args = new Dictionary<string, object?> { ["q"] = doc.RootElement.Clone() };
        ToolArgumentReader.GetString(args, "q").Should().Be("json_string");
    }

    [Fact]
    public void GetString_FromJsonElementNonString_ReturnsRaw()
    {
        using var doc = JsonDocument.Parse("42");
        var args = new Dictionary<string, object?> { ["q"] = doc.RootElement.Clone() };
        var val = ToolArgumentReader.GetString(args, "q");
        val.Should().Be("42");
    }

    [Fact]
    public void GetString_FromOtherType_CallsToString()
    {
        var args = new Dictionary<string, object?> { ["q"] = 99 };
        ToolArgumentReader.GetString(args, "q").Should().Be("99");
    }

    [Fact]
    public void GetInt_ReturnsIntValue()
    {
        var args = new Dictionary<string, object?> { ["n"] = 42 };
        ToolArgumentReader.GetInt(args, "n").Should().Be(42);
    }

    [Fact]
    public void GetInt_FromLong_CastsCorrectly()
    {
        var args = new Dictionary<string, object?> { ["n"] = 100L };
        ToolArgumentReader.GetInt(args, "n").Should().Be(100);
    }

    [Fact]
    public void GetInt_FromDouble_Converts()
    {
        var args = new Dictionary<string, object?> { ["n"] = 7.0 };
        ToolArgumentReader.GetInt(args, "n").Should().Be(7);
    }

    [Fact]
    public void GetInt_FromString_ParsesCorrectly()
    {
        var args = new Dictionary<string, object?> { ["n"] = "99" };
        ToolArgumentReader.GetInt(args, "n").Should().Be(99);
    }

    [Fact]
    public void GetInt_InvalidString_ReturnsNull()
    {
        var args = new Dictionary<string, object?> { ["n"] = "abc" };
        ToolArgumentReader.GetInt(args, "n").Should().BeNull();
    }

    [Fact]
    public void GetInt_MissingKey_ReturnsNull()
    {
        var args = new Dictionary<string, object?>();
        ToolArgumentReader.GetInt(args, "n").Should().BeNull();
    }

    [Fact]
    public void GetInt_FromJsonElement_ReturnsInt()
    {
        using var doc = JsonDocument.Parse("55");
        var args = new Dictionary<string, object?> { ["n"] = doc.RootElement.Clone() };
        ToolArgumentReader.GetInt(args, "n").Should().Be(55);
    }

    [Fact]
    public void GetBool_ReturnsBoolValue()
    {
        var args = new Dictionary<string, object?> { ["b"] = true };
        ToolArgumentReader.GetBool(args, "b").Should().BeTrue();
    }

    [Fact]
    public void GetBool_FromString_ParsesCorrectly()
    {
        var args = new Dictionary<string, object?> { ["b"] = "false" };
        ToolArgumentReader.GetBool(args, "b").Should().BeFalse();
    }

    [Fact]
    public void GetBool_FromJsonElementTrue_ReturnsTrue()
    {
        using var doc = JsonDocument.Parse("true");
        var args = new Dictionary<string, object?> { ["b"] = doc.RootElement.Clone() };
        ToolArgumentReader.GetBool(args, "b").Should().BeTrue();
    }

    [Fact]
    public void GetBool_FromJsonElementFalse_ReturnsFalse()
    {
        using var doc = JsonDocument.Parse("false");
        var args = new Dictionary<string, object?> { ["b"] = doc.RootElement.Clone() };
        ToolArgumentReader.GetBool(args, "b").Should().BeFalse();
    }

    [Fact]
    public void GetBool_MissingKey_ReturnsNull()
    {
        var args = new Dictionary<string, object?>();
        ToolArgumentReader.GetBool(args, "b").Should().BeNull();
    }

    [Fact]
    public void GetBool_InvalidValue_ReturnsNull()
    {
        var args = new Dictionary<string, object?> { ["b"] = new object() };
        ToolArgumentReader.GetBool(args, "b").Should().BeNull();
    }

    [Fact]
    public void GetDouble_ReturnsDoubleValue()
    {
        var args = new Dictionary<string, object?> { ["d"] = 3.14 };
        ToolArgumentReader.GetDouble(args, "d").Should().Be(3.14);
    }

    [Fact]
    public void GetDouble_FromInt_ConvertsProperly()
    {
        var args = new Dictionary<string, object?> { ["d"] = 5 };
        ToolArgumentReader.GetDouble(args, "d").Should().Be(5.0);
    }

    [Fact]
    public void GetDouble_FromLong_ConvertsProperly()
    {
        var args = new Dictionary<string, object?> { ["d"] = 10L };
        ToolArgumentReader.GetDouble(args, "d").Should().Be(10.0);
    }

    [Fact]
    public void GetDouble_FromJsonElement_ReturnsDouble()
    {
        using var doc = JsonDocument.Parse("2.5");
        var args = new Dictionary<string, object?> { ["d"] = doc.RootElement.Clone() };
        ToolArgumentReader.GetDouble(args, "d").Should().Be(2.5);
    }

    [Fact]
    public void GetDouble_FromString_ParsesCorrectly()
    {
        var args = new Dictionary<string, object?> { ["d"] = "1.23" };
        ToolArgumentReader.GetDouble(args, "d").Should().BeApproximately(1.23, 0.001);
    }

    [Fact]
    public void GetDouble_MissingKey_ReturnsNull()
    {
        var args = new Dictionary<string, object?>();
        ToolArgumentReader.GetDouble(args, "d").Should().BeNull();
    }

    [Fact]
    public void GetJson_ReturnsJsonString()
    {
        using var doc = JsonDocument.Parse("[1,2,3]");
        var args = new Dictionary<string, object?> { ["items"] = doc.RootElement.Clone() };
        var val = ToolArgumentReader.GetJson(args, "items");
        val.Should().Contain("[");
    }

    [Fact]
    public void GetJson_FromString_ReturnsAsIs()
    {
        var args = new Dictionary<string, object?> { ["items"] = "[1,2]" };
        ToolArgumentReader.GetJson(args, "items").Should().Be("[1,2]");
    }

    [Fact]
    public void GetJson_NullValue_ReturnsNull()
    {
        var args = new Dictionary<string, object?> { ["items"] = null };
        ToolArgumentReader.GetJson(args, "items").Should().BeNull();
    }

    [Fact]
    public void GetJson_MissingKey_ReturnsNull()
    {
        var args = new Dictionary<string, object?>();
        ToolArgumentReader.GetJson(args, "items").Should().BeNull();
    }

    [Fact]
    public void GetJson_OtherType_Serializes()
    {
        var args = new Dictionary<string, object?> { ["items"] = new int[] { 1, 2, 3 } };
        var val = ToolArgumentReader.GetJson(args, "items");
        val.Should().NotBeNullOrEmpty();
    }
}
