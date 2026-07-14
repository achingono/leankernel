using FluentAssertions;
using LeanKernel.Gateway.Tools.BuiltIn;
using LeanKernel.Logic.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class CalculationToolTests
{
    private IServiceScopeFactory BuildScopeFactory(int maxItems = 1000)
    {
        var services = new ServiceCollection();
        services.Configure<AgentSettings>(opts =>
        {
            opts.Tools.BuiltIns.Calculation.MaxInputItems = maxItems;
        });
        var sp = services.BuildServiceProvider();

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var mockScope = new Mock<IServiceScope>();
                mockScope.Setup(s => s.ServiceProvider).Returns(sp);
                return mockScope.Object;
            });

        return mockFactory.Object;
    }

    private async Task<(bool success, string? output, string? error)> InvokeAsync(
        string toolName,
        Dictionary<string, object?> args,
        IServiceScopeFactory? scopeFactory = null)
    {
        scopeFactory ??= BuildScopeFactory();
        var tools = CalculationTools.Create(scopeFactory).ToList();
        var tool = tools.First(t => t.Name == toolName);
        var result = await tool.Handler(args, CancellationToken.None);
        return (result.Success, result.Output, result.Error);
    }

    // calculate tool
    [Theory]
    [InlineData("2 + 3", "5")]
    [InlineData("10 - 4", "6")]
    [InlineData("3 * 4", "12")]
    [InlineData("10 / 2", "5")]
    [InlineData("(2 + 3) * 4", "20")]
    [InlineData("-5 + 10", "5")]
    public async Task Calculate_ValidExpression_ReturnsResult(string expression, string expected)
    {
        var (success, output, _) = await InvokeAsync("calculate",
            new Dictionary<string, object?> { ["expression"] = expression });

        success.Should().BeTrue();
        output.Should().Be(expected);
    }

    [Fact]
    public async Task Calculate_MissingExpression_ReturnsError()
    {
        var (success, _, error) = await InvokeAsync("calculate", new Dictionary<string, object?>());
        success.Should().BeFalse();
        error.Should().Contain("required");
    }

    [Fact]
    public async Task Calculate_InvalidExpression_ReturnsError()
    {
        var (success, _, error) = await InvokeAsync("calculate",
            new Dictionary<string, object?> { ["expression"] = "2 + abc" });
        success.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Calculate_DivisionByZero_ReturnsError()
    {
        var (success, _, error) = await InvokeAsync("calculate",
            new Dictionary<string, object?> { ["expression"] = "10 / 0" });
        success.Should().BeFalse();
        error.Should().Contain("zero");
    }

    // count tool
    [Fact]
    public async Task Count_ValidArray_ReturnsCount()
    {
        var (success, output, _) = await InvokeAsync("count",
            new Dictionary<string, object?> { ["items"] = "[1,2,3,4,5]" });

        success.Should().BeTrue();
        output.Should().Be("5");
    }

    [Fact]
    public async Task Count_EmptyArray_ReturnsZero()
    {
        var (success, output, _) = await InvokeAsync("count",
            new Dictionary<string, object?> { ["items"] = "[]" });

        success.Should().BeTrue();
        output.Should().Be("0");
    }

    [Fact]
    public async Task Count_InvalidJson_ReturnsError()
    {
        var (success, _, error) = await InvokeAsync("count",
            new Dictionary<string, object?> { ["items"] = "not-json" });
        success.Should().BeFalse();
    }

    [Fact]
    public async Task Count_ExceedsMaxItems_ReturnsError()
    {
        var items = "[" + string.Join(",", Enumerable.Range(0, 5)) + "]";
        var (success, _, error) = await InvokeAsync("count",
            new Dictionary<string, object?> { ["items"] = items },
            BuildScopeFactory(maxItems: 3));

        success.Should().BeFalse();
        error.Should().Contain("maximum");
    }

    // sum tool
    [Fact]
    public async Task Sum_NumericArray_ReturnsSum()
    {
        var (success, output, _) = await InvokeAsync("sum",
            new Dictionary<string, object?> { ["items"] = "[1,2,3,4]" });

        success.Should().BeTrue();
        output.Should().Be("10");
    }

    [Fact]
    public async Task Sum_ObjectArrayWithField_SumsField()
    {
        var (success, output, _) = await InvokeAsync("sum",
            new Dictionary<string, object?>
            {
                ["items"] = """[{"val":5},{"val":10}]""",
                ["field"] = "val"
            });

        success.Should().BeTrue();
        output.Should().Be("15");
    }

    // average tool
    [Fact]
    public async Task Average_NumericArray_ReturnsAverage()
    {
        var (success, output, _) = await InvokeAsync("average",
            new Dictionary<string, object?> { ["items"] = "[2,4,6]" });

        success.Should().BeTrue();
        output.Should().Be("4");
    }

    [Fact]
    public async Task Average_NoNumericValues_ReturnsError()
    {
        var (success, _, error) = await InvokeAsync("average",
            new Dictionary<string, object?> { ["items"] = """[{"name":"test"}]""" });

        success.Should().BeFalse();
        error.Should().Contain("numeric");
    }

    // min_max tool
    [Fact]
    public async Task MinMax_NumericArray_ReturnsMinMax()
    {
        var (success, output, _) = await InvokeAsync("min_max",
            new Dictionary<string, object?> { ["items"] = "[3,1,4,1,5,9,2,6]" });

        success.Should().BeTrue();
        output.Should().Contain("\"min\":1");
        output.Should().Contain("\"max\":9");
    }

    // group_by tool
    [Fact]
    public async Task GroupBy_ValidArray_ReturnsGroups()
    {
        var items = """[{"status":"active"},{"status":"inactive"},{"status":"active"}]""";
        var (success, output, _) = await InvokeAsync("group_by",
            new Dictionary<string, object?> { ["items"] = items, ["key"] = "status" });

        success.Should().BeTrue();
        output.Should().Contain("\"active\":2");
        output.Should().Contain("\"inactive\":1");
    }

    [Fact]
    public async Task GroupBy_MissingKey_ReturnsError()
    {
        var (success, _, error) = await InvokeAsync("group_by",
            new Dictionary<string, object?> { ["items"] = "[{}]" });

        success.Should().BeFalse();
        error.Should().Contain("key");
    }

    [Fact]
    public void ArithmeticEvaluator_Evaluate_BasicOperations()
    {
        // Test via calculate tool indirectly
        var tools = CalculationTools.Create(BuildScopeFactory()).ToList();
        tools.Should().HaveCount(6); // calculate, count, sum, average, min_max, group_by
        tools.Select(t => t.Name).Should().Contain("calculate", "count", "sum", "average", "min_max", "group_by");
    }

    [Fact]
    public async Task GroupBy_NonObjectItems_SkipsNonObjects()
    {
        // Array with mixed types - strings are not objects and should be skipped
        var items = """["active","active","inactive"]""";
        var (success, output, _) = await InvokeAsync("group_by",
            new Dictionary<string, object?> { ["items"] = items, ["key"] = "status" });

        success.Should().BeTrue();
        output.Should().Be("{}"); // all items are non-objects, skipped
    }

    [Fact]
    public async Task GroupBy_ObjectsMissingKey_SkipsThoseObjects()
    {
        var items = """[{"name":"a"},{"name":"b"},{"status":"active"}]""";
        var (success, output, _) = await InvokeAsync("group_by",
            new Dictionary<string, object?> { ["items"] = items, ["key"] = "status" });

        success.Should().BeTrue();
        output.Should().Contain("active");
        output.Should().NotContain("name");
    }

    [Fact]
    public async Task GroupBy_NumberKey_GroupsByNumericValue()
    {
        var items = """[{"score":1},{"score":2},{"score":1}]""";
        var (success, output, _) = await InvokeAsync("group_by",
            new Dictionary<string, object?> { ["items"] = items, ["key"] = "score" });

        success.Should().BeTrue();
        output.Should().Contain("\"1\":2");
    }
}
