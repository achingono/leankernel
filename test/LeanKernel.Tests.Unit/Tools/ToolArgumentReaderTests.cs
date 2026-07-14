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
    public void GetInt_ReturnsIntValue()
    {
        var args = new Dictionary<string, object?> { ["n"] = 42 };
        ToolArgumentReader.GetInt(args, "n").Should().Be(42);
    }

    [Fact]
    public void GetInt_FromString_ParsesCorrectly()
    {
        var args = new Dictionary<string, object?> { ["n"] = "99" };
        ToolArgumentReader.GetInt(args, "n").Should().Be(99);
    }

    [Fact]
    public void GetInt_MissingKey_ReturnsNull()
    {
        var args = new Dictionary<string, object?>();
        ToolArgumentReader.GetInt(args, "n").Should().BeNull();
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
}
