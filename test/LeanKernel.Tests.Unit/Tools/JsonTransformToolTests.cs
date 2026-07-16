using System.Text.Json;
using FluentAssertions;
using LeanKernel.Logic.Tools;
using Xunit;
using LeanKernel.Logic.Tools.BuiltIn.Data;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class JsonTransformToolTests
{
    private static readonly IServiceScopeFactory StubScopeFactory = Mock.Of<IServiceScopeFactory>();

    private static async Task<ToolResult> InvokeAsync(
        Dictionary<string, object?> args,
        CancellationToken ct = default)
    {
        var tool = JsonTransformTool.Create(StubScopeFactory);
        return await tool.Handler(args, ct);
    }

    private static JsonElement ToJsonElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Create_DefinesCorrectMetadata()
    {
        var tool = JsonTransformTool.Create(StubScopeFactory);
        tool.Name.Should().Be("json_transform");
        tool.Category.Should().Be("data");
        tool.Parameters.Should().HaveCount(2);
        tool.Parameters.Select(p => p.Name).Should().Contain(["input", "operations"]);
    }

    [Fact]
    public async Task Select_DotPath_ReturnsNestedValue()
    {
        var input = """{"user":{"name":"Alice","age":30}}""";
        var ops = """[{"op":"select","path":"user.name"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"Alice\"");
    }

    [Fact]
    public async Task Select_NonExistentPath_ReturnsNull()
    {
        var input = """{"user":{"name":"Alice"}}""";
        var ops = """[{"op":"select","path":"user.missing"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Be("null");
    }

    [Fact]
    public async Task Select_ArrayIndex_ReturnsElement()
    {
        var input = """[10, 20, 30]""";
        var ops = """[{"op":"select","path":"1"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Be("20");
    }

    [Fact]
    public async Task Select_MissingPath_Fails()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"a":1}""",
            ["operations"] = """[{"op":"select"}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Missing 'path'");
    }

    [Fact]
    public async Task Project_Fields_ExtractsSubsetOfObjects()
    {
        var input = """[{"name":"Alice","age":30,"email":"a@b.com"},{"name":"Bob","age":25,"email":"b@b.com"}]""";
        var ops = """[{"op":"project","fields":["name","email"]}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var arr = doc.RootElement;
        arr.GetArrayLength().Should().Be(2);
        var first = arr[0];
        first.GetProperty("name").GetString().Should().Be("Alice");
        first.GetProperty("email").GetString().Should().Be("a@b.com");
        first.EnumerateObject().Count().Should().Be(2);
    }

    [Fact]
    public async Task Project_NonArrayInput_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"name":"Alice"}""",
            ["operations"] = """[{"op":"project","fields":["name"]}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("array");
    }

    [Fact]
    public async Task Project_MissingFieldsArray_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """[{"name":"Alice"}]""",
            ["operations"] = """[{"op":"project"}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("'fields'");
    }

    [Fact]
    public async Task FilterEquals_MatchingValues_ReturnsFiltered()
    {
        var input = """[{"status":"active","name":"Alice"},{"status":"inactive","name":"Bob"},{"status":"active","name":"Carol"}]""";
        var ops = """[{"op":"filter_equals","field":"status","value":"active"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(2);
        doc.RootElement[0].GetProperty("name").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task FilterEquals_NoMatch_ReturnsEmptyArray()
    {
        var input = """[{"status":"active"},{"status":"inactive"}]""";
        var ops = """[{"op":"filter_equals","field":"status","value":"deleted"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task FilterEquals_MissingField_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """[{"a":1}]""",
            ["operations"] = """[{"op":"filter_equals","value":"x"}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("'field'");
    }

    [Fact]
    public async Task FilterEquals_MissingValue_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """[{"a":1}]""",
            ["operations"] = """[{"op":"filter_equals","field":"a"}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("'value'");
    }

    [Fact]
    public async Task FilterEquals_NonArrayInput_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"a":1}""",
            ["operations"] = """[{"op":"filter_equals","field":"a","value":1}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("array");
    }

    [Fact]
    public async Task Sort_Ascending_ReturnsSorted()
    {
        var input = """[{"name":"Charlie"},{"name":"Alice"},{"name":"Bob"}]""";
        var ops = """[{"op":"sort","field":"name"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement[0].GetProperty("name").GetString().Should().Be("Alice");
        doc.RootElement[1].GetProperty("name").GetString().Should().Be("Bob");
        doc.RootElement[2].GetProperty("name").GetString().Should().Be("Charlie");
    }

    [Fact]
    public async Task Sort_Descending_ReturnsReversed()
    {
        var input = """[{"val":1},{"val":3},{"val":2}]""";
        var ops = """[{"op":"sort","field":"val","descending":true}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement[0].GetProperty("val").GetInt32().Should().Be(3);
        doc.RootElement[1].GetProperty("val").GetInt32().Should().Be(2);
        doc.RootElement[2].GetProperty("val").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Sort_MissingField_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """[{"a":1}]""",
            ["operations"] = """[{"op":"sort"}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("'field'");
    }

    [Fact]
    public async Task Sort_NonArrayInput_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"a":1}""",
            ["operations"] = """[{"op":"sort","field":"a"}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("array");
    }

    [Fact]
    public async Task Sort_NumericValues_SortsCorrectly()
    {
        var input = """[{"score":50},{"score":10},{"score":30}]""";
        var ops = """[{"op":"sort","field":"score"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement[0].GetProperty("score").GetInt32().Should().Be(10);
        doc.RootElement[1].GetProperty("score").GetInt32().Should().Be(30);
        doc.RootElement[2].GetProperty("score").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task Slice_DefaultParameters_ReturnsAll()
    {
        var input = """[1, 2, 3, 4, 5]""";
        var ops = """[{"op":"slice"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task Slice_WithStartEnd_ReturnsRange()
    {
        var input = """[1, 2, 3, 4, 5]""";
        var ops = """[{"op":"slice","start":1,"end":4}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(3);
        doc.RootElement[0].GetInt32().Should().Be(2);
        doc.RootElement[2].GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Slice_NonArrayInput_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"a":1}""",
            ["operations"] = """[{"op":"slice","start":0,"end":1}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("array");
    }

    [Fact]
    public async Task Slice_OutOfBounds_Clamped()
    {
        var input = """[1, 2, 3]""";
        var ops = """[{"op":"slice","start":0,"end":100}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Flatten_SingleDepth_Flattens()
    {
        var input = """[[1,2],[3,[4,5]]]""";
        var ops = """[{"op":"flatten","depth":1}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(4);
        doc.RootElement[0].GetInt32().Should().Be(1);
        doc.RootElement[3].ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement[3].GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Flatten_Deep_FlattensAll()
    {
        var input = """[[[1,[2]],[3]],[4]]""";
        var ops = """[{"op":"flatten","depth":10}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(4);
        doc.RootElement[0].GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Flatten_NonArrayInput_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"a":1}""",
            ["operations"] = """[{"op":"flatten"}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("array");
    }

    [Fact]
    public async Task Flatten_NoDepth_DefaultsToOne()
    {
        var input = """[[1,2],[3,4]]""";
        var ops = """[{"op":"flatten"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task MultipleOperations_Chained()
    {
        var input = """[{"name":"Alice","score":90},{"name":"Bob","score":70},{"name":"Carol","score":85}]""";
        var ops = """
        [
            {"op":"filter_equals","field":"score","value":90},
            {"op":"project","fields":["name"]}
        ]
        """;
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task SortThenSlice_ReturnsTopN()
    {
        var input = """[{"name":"Alice","score":90},{"name":"Bob","score":70},{"name":"Carol","score":85}]""";
        var ops = """
        [
            {"op":"sort","field":"score","descending":true},
            {"op":"slice","start":0,"end":2}
        ]
        """;
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(2);
        doc.RootElement[0].GetProperty("name").GetString().Should().Be("Alice");
        doc.RootElement[1].GetProperty("name").GetString().Should().Be("Carol");
    }

    [Fact]
    public async Task TooManyOperations_ReturnsError()
    {
        var ops = "[" + string.Join(",", Enumerable.Range(0, 51).Select(i => $"{{\"op\":\"slice\",\"start\":0,\"end\":1}}")) + "]";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """[1]""",
            ["operations"] = ops
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Too many operations");
    }

    [Fact]
    public async Task UnknownOperation_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"a":1}""",
            ["operations"] = """[{"op":"unknown_op"}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown operation");
    }

    [Fact]
    public async Task MissingInput_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operations"] = """[{"op":"slice"}]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Input is required");
    }

    [Fact]
    public async Task MissingOperations_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"a":1}"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Operations are required");
    }

    [Fact]
    public async Task EmptyOperations_ReturnsInputUnchanged()
    {
        var input = """{"key":"value"}""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = "[]"
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("\"key\"");
        result.Output.Should().Contain("\"value\"");
    }

    [Fact]
    public async Task InvalidInputJSON_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = "not-json",
            ["operations"] = "[]"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid input JSON");
    }

    [Fact]
    public async Task InvalidOperationsJSON_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"a":1}""",
            ["operations"] = "not-json"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid operations JSON");
    }

    [Fact]
    public async Task NullInput_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = null,
            ["operations"] = "[]"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Input is required");
    }

    [Fact]
    public async Task OperationNonObject_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = """{"a":1}""",
            ["operations"] = """["not-an-object"]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("JSON object");
    }

    [Fact]
    public async Task SelectFromDictionary_Input_Works()
    {
        var dict = new Dictionary<string, object?>
        {
            ["input"] = """{"a":{"b":42}}""",
            ["operations"] = """[{"op":"select","path":"a.b"}]"""
        };
        var result = await InvokeAsync(dict);
        result.Success.Should().BeTrue();
        result.Output.Should().Be("42");
    }

    [Fact]
    public async Task SelectFromJsonElementInput_Works()
    {
        var inputEl = ToJsonElement("""{"x":{"y":"found"}}""");
        var opsEl = ToJsonElement("""[{"op":"select","path":"x.y"}]""");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = inputEl,
            ["operations"] = opsEl
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"found\"");
    }

    [Fact]
    public async Task ProjectFromListInput_Works()
    {
        var list = new List<object?>
        {
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "A" },
            new Dictionary<string, object?> { ["id"] = 2, ["name"] = "B" }
        };
        var opsEl = ToJsonElement("""[{"op":"project","fields":["name"]}]""");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = list,
            ["operations"] = opsEl
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task CancelledToken_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["input"] = """{"a":1}""",
                ["operations"] = """[]"""
            },
            cts.Token);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SelectDeepNestedPath_ReturnsValue()
    {
        var input = """{"l1":{"l2":{"l3":{"l4":"deep"}}}}""";
        var ops = """[{"op":"select","path":"l1.l2.l3.l4"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"deep\"");
    }

    [Fact]
    public async Task SortWithMissingField_SortsToEnd()
    {
        var input = """[{"name":"Alice","score":90},{"name":"Bob"},{"name":"Carol","score":85}]""";
        var ops = """[{"op":"sort","field":"score"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(3);
        doc.RootElement[0].GetProperty("name").GetString().Should().Be("Bob");
    }

    [Fact]
    public async Task Slice_NegativeStart_ClampedToZero()
    {
        var input = """[1, 2, 3]""";
        var ops = """[{"op":"slice","start":-5,"end":2}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Slice_EndBeforeStart_ClampsToEnd()
    {
        var input = """[1, 2, 3, 4, 5]""";
        var ops = """[{"op":"slice","start":3,"end":1}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task FilterEquals_NullFilterValue_MatchesNulls()
    {
        var input = """[{"val":1},{"val":null},{"val":3}]""";
        var ops = """[{"op":"filter_equals","field":"val","value":null}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ProjectSkipsNonObjectItems()
    {
        var input = """[{"name":"Alice"},null,123,{"name":"Bob"}]""";
        var ops = """[{"op":"project","fields":["name"]}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ProjectFromJsonElementInput_Works()
    {
        var inputEl = ToJsonElement("""[{"id":1,"val":"x"},{"id":2,"val":"y"}]""");
        var opsEl = ToJsonElement("""[{"op":"project","fields":["val"]}]""");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = inputEl,
            ["operations"] = opsEl
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(2);
        doc.RootElement[0].GetProperty("val").GetString().Should().Be("x");
    }

    [Fact]
    public async Task SortStablePreservesEqualOrder()
    {
        var input = """[{"g":"a","i":1},{"g":"a","i":2},{"g":"b","i":3},{"g":"a","i":4}]""";
        var ops = """[{"op":"sort","field":"g"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var indices = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("i").GetInt32()).ToList();
        indices.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task FlattenDepthZero_ReturnsArrayUnchanged()
    {
        var input = """[[1,[2]],3]""";
        var ops = """[{"op":"flatten","depth":0}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetArrayLength().Should().Be(2);
        doc.RootElement[0].ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement[0].GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task SelectNestedArrayIndex_Works()
    {
        var input = """{"items":["alpha","beta","gamma"]}""";
        var ops = """[{"op":"select","path":"items.2"}]""";
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = input,
            ["operations"] = ops
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"gamma\"");
    }
}
