using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using LeanKernel.Tools.BuiltIn.Data;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tests.Unit.Tools;

public class JsonTransformToolTests
{
    [Fact]
    public async Task JsonTransformTool_runs_select_for_json_string_input()
    {
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"user\":{\"name\":\"Ada\"}}",
            ["operations"] = ParseArray("""[{ "op":"select", "path":"user.name" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"Ada\"");
    }

    [Fact]
    public async Task JsonTransformTool_accepts_native_object_input()
    {
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["value"] = 42 },
            ["operations"] = ParseArray("""[{ "op":"select", "path":"value" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("42");
    }

    [Fact]
    public async Task JsonTransformTool_project_sets_null_for_missing_fields()
    {
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "id": 7 }"""),
            ["operations"] = ParseArray("""
            [{ "op":"project", "fields":[{"name":"id","path":"id"},{"name":"name","path":"name"}]}]
            """)
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"id":7,"name":null}""");
    }

    [Fact]
    public async Task JsonTransformTool_filter_equals_ignores_missing_path()
    {
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "kind":"a" },{ "kind":"b" },{ "name":"x" }]"""),
            ["operations"] = ParseArray("""[{ "op":"filter_equals", "path":"kind", "value":"a" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"kind":"a"}]""");
    }

    [Fact]
    public async Task JsonTransformTool_sort_places_missing_path_last()
    {
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "rank":2 },{ "name":"none" },{ "rank":1 }]"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"rank", "direction":"asc" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"rank":1},{"rank":2},{"name":"none"}]""");
    }

    [Fact]
    public async Task JsonTransformTool_slice_returns_expected_window()
    {
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[1,2,3,4,5]"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":1, "limit":3 }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("[2,3,4]");
    }

    [Fact]
    public async Task JsonTransformTool_flatten_flattens_one_level_when_target_is_array_of_arrays()
    {
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "groups":[[1,2],[3]] }"""),
            ["operations"] = ParseArray("""[{ "op":"flatten", "path":"groups" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"groups":[1,2,3]}""");
    }

    [Fact]
    public async Task JsonTransformTool_returns_null_for_select_missing_path()
    {
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "user":{} }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"user.name" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("null");
    }

    [Fact]
    public async Task JsonTransformTool_returns_validation_error_for_invalid_path_grammar()
    {
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"$.a" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unsupported syntax");
    }

    [Fact]
    public async Task JsonTransformTool_returns_validation_error_for_operation_limit()
    {
        var operations = new JsonArray();
        for (var i = 0; i < 51; i++)
        {
            operations.Add(ParseObject("""{ "op":"select", "path":"a" }"""));
        }

        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = operations
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("maximum count");
    }

    [Fact]
    public async Task JsonTransformTool_returns_validation_error_when_output_exceeds_limit()
    {
        var payload = new string('x', 210_000);
        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject($$"""{"data":"{{payload}}"}"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"data" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("exceeds");
    }

    [Fact]
    public async Task JsonTransformTool_accepts_operations_as_json_element_array()
    {
        using var doc = JsonDocument.Parse("""[{ "op":"select", "path":"name" }]""");
        var operations = doc.RootElement.Clone();

        var result = await ExecuteAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{"name":"json-element"}"""),
            ["operations"] = operations
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"json-element\"");
    }

    private static async Task<LeanKernel.Abstractions.Models.ToolResult> ExecuteAsync(Dictionary<string, object?> args)
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var tool = JsonTransformTool.Create(provider.GetRequiredService<IServiceScopeFactory>());
        return await tool.Handler!(args, CancellationToken.None);
    }

    private static JsonArray ParseArray(string json) => JsonNode.Parse(json)!.AsArray();
    private static JsonObject ParseObject(string json) => JsonNode.Parse(json)!.AsObject();
}
