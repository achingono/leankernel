using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Data;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class JsonTransformToolTests
{
    // ----------------------------------------------------------- Create + metadata

    [Fact]
    public void Create_returns_correct_metadata()
    {
        var tool = JsonTransformTool.Create(Mock.Of<IServiceScopeFactory>());

        tool.Name.Should().Be("json_transform");
        tool.Description.Should().Be("Apply deterministic transforms to JSON payloads");
        tool.Category.Should().Be("data");
        tool.Parameters.Should().HaveCount(2);
        tool.Parameters.Should().Contain(p => p.Name == "input" && p.Type == "object" && p.Required);
        tool.Parameters.Should().Contain(p => p.Name == "operations" && p.Type == "array" && p.Required);
        tool.Handler.Should().NotBeNull();
    }

    [Fact]
    public void Create_throws_when_scopeFactory_is_null()
    {
        var act = () => JsonTransformTool.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ----------------------------------------------------------- Input validation

    [Fact]
    public async Task missing_input_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operations"] = ParseArray("""[{ "op":"select", "path":"a" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("input is required");
    }

    [Fact]
    public async Task null_input_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = null!,
            ["operations"] = ParseArray("""[{ "op":"select", "path":"a" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("input is required");
    }

    [Fact]
    public async Task invalid_json_string_input_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = "not valid json",
            ["operations"] = ParseArray("""[{ "op":"select", "path":"a" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("input must be valid JSON");
    }

    // ----------------------------------------------------------- Operations validation

    [Fact]
    public async Task missing_operations_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("operations must be an array");
    }

    [Fact]
    public async Task empty_operations_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = new JsonArray()
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("operations must contain at least one operation");
    }

    [Fact]
    public async Task too_many_operations_returns_error()
    {
        var operations = new JsonArray();
        for (var i = 0; i < 51; i++)
        {
            operations.Add(ParseObject("""{ "op":"select", "path":"a" }"""));
        }

        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = operations
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("maximum count of 50");
    }

    [Fact]
    public async Task non_object_operation_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[ "not_an_object" ]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("operation 1 must be an object");
    }

    [Fact]
    public async Task operation_missing_op_and_type_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "path":"a" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("operation 1 must define 'op' or 'type'");
    }

    [Fact]
    public async Task unsupported_operation_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"unsupported_op" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("operation 1 uses unsupported op 'unsupported_op'");
    }

    // ----------------------------------------------------------- Select operation

    [Fact]
    public async Task select_nested_path_returns_value()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "user":{ "name":"Ada", "age":42 } }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"user.name" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"Ada\"");
    }

    [Fact]
    public async Task select_missing_path_returns_null()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "user":{} }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"user.name" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("null");
    }

    [Fact]
    public async Task select_with_array_index_returns_element()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "items":[10,20,30] }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"items[1]" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("20");
    }

    // ----------------------------------------------------------- Project operation

    [Fact]
    public async Task project_with_fields_returns_projected_object()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "id":7, "name":"Alice", "extra":"ignored" }"""),
            ["operations"] = ParseArray("""
            [{ "op":"project", "fields":[
                {"name":"id","path":"id"},
                {"name":"label","path":"name"}
            ]}]
            """)
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"id":7,"label":"Alice"}""");
    }

    [Fact]
    public async Task project_field_missing_name_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"project", "fields":[{"path":"a"}] }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("missing name");
    }

    [Fact]
    public async Task project_field_missing_path_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"project", "fields":[{"name":"x"}] }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("missing path");
    }

    // ----------------------------------------------------------- FilterEquals operation

    [Fact]
    public async Task filter_equals_returns_matching_items()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "kind":"a" },{ "kind":"b" },{ "kind":"a" }]"""),
            ["operations"] = ParseArray("""[{ "op":"filter_equals", "path":"kind", "value":"a" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"kind":"a"},{"kind":"a"}]""");
    }

    [Fact]
    public async Task filter_equals_on_non_array_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "kind":"a" }"""),
            ["operations"] = ParseArray("""[{ "op":"filter_equals", "path":"kind", "value":"a" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("filter_equals requires current input to be an array");
    }

    [Fact]
    public async Task filter_equals_missing_path_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "kind":"a" }]"""),
            ["operations"] = ParseArray("""[{ "op":"filter_equals", "value":"a" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("operation path is required");
    }

    // ----------------------------------------------------------- Sort operation

    [Fact]
    public async Task sort_ascending_returns_correct_order()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "rank":3 },{ "rank":1 },{ "rank":2 }]"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"rank", "direction":"asc" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"rank":1},{"rank":2},{"rank":3}]""");
    }

    [Fact]
    public async Task sort_descending_returns_reverse_order()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "rank":1 },{ "rank":3 },{ "rank":2 }]"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"rank", "direction":"desc" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"rank":3},{"rank":2},{"rank":1}]""");
    }

    [Fact]
    public async Task sort_missing_path_values_go_last()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "rank":2 },{ "name":"none" },{ "rank":1 }]"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"rank", "direction":"asc" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"rank":1},{"rank":2},{"name":"none"}]""");
    }

    [Fact]
    public async Task sort_invalid_direction_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "rank":1 }]"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"rank", "direction":"invalid" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("sort direction must be 'asc' or 'desc'");
    }

    [Fact]
    public async Task sort_non_array_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "rank":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"rank" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("sort requires current input to be an array");
    }

    // ----------------------------------------------------------- Slice operation

    [Fact]
    public async Task slice_with_offset_and_limit_returns_subset()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[1,2,3,4,5]"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":1, "limit":3 }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("[2,3,4]");
    }

    [Fact]
    public async Task slice_offset_beyond_count_returns_empty()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[1,2,3]"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":10, "limit":5 }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("[]");
    }

    [Fact]
    public async Task slice_missing_limit_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[1,2,3]"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":0 }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("slice limit must be an integer");
    }

    [Fact]
    public async Task slice_negative_limit_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[1,2,3]"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":0, "limit":-1 }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("slice limit must be non-negative");
    }

    [Fact]
    public async Task slice_non_array_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":0, "limit":5 }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("slice requires current input to be an array");
    }

    [Fact]
    public async Task slice_negative_offset_is_clamped_to_zero()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[1,2,3]"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":-5, "limit":2 }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("[1,2]");
    }

    // ----------------------------------------------------------- Flatten operation

    [Fact]
    public async Task flatten_nested_array_flattens_one_level()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "groups":[[1,2],[3],[4,5]] }"""),
            ["operations"] = ParseArray("""[{ "op":"flatten", "path":"groups" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"groups":[1,2,3,4,5]}""");
    }

    [Fact]
    public async Task flatten_with_non_array_items_returns_no_change()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "groups":[1,2,3] }"""),
            ["operations"] = ParseArray("""[{ "op":"flatten", "path":"groups" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"groups":[1,2,3]}""");
    }

    [Fact]
    public async Task flatten_missing_path_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "groups":[[1]] }"""),
            ["operations"] = ParseArray("""[{ "op":"flatten" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("operation path is required");
    }

    // ----------------------------------------------------------- Edge cases

    [Fact]
    public async Task cancellation_throws_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = "{}",
            ["operations"] = ParseArray("""[{ "op":"select", "path":"a" }]""")
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task output_exceeding_maximum_returns_error()
    {
        var payload = new string('x', 210_000);
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject($$"""{"data":"{{payload}}"}"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"data" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("exceeds 200000 characters");
    }

    [Fact]
    public async Task multiple_operations_chained_produces_correct_result()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""
            {
                "users": [
                    { "name":"Bob",   "role":"user",  "score":80 },
                    { "name":"Alice", "role":"admin", "score":95 },
                    { "name":"Carol", "role":"admin", "score":90 }
                ]
            }
            """),
            ["operations"] = ParseArray("""
            [
                { "op":"select", "path":"users" },
                { "op":"filter_equals", "path":"role", "value":"admin" },
                { "op":"sort", "path":"score", "direction":"desc" },
                { "op":"slice", "offset":0, "limit":1 }
            ]
            """)
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"name":"Alice","role":"admin","score":95}]""");
    }

    [Fact]
    public async Task operations_as_json_element_array_is_accepted()
    {
        using var doc = JsonDocument.Parse("""[{ "op":"select", "path":"name" }]""");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{"name":"json-element"}"""),
            ["operations"] = doc.RootElement.Clone()
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"json-element\"");
    }

    [Fact]
    public async Task native_dictionary_input_is_accepted()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["value"] = 42 },
            ["operations"] = ParseArray("""[{ "op":"select", "path":"value" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("42");
    }

    [Fact]
    public async Task project_sets_null_for_missing_fields()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "id":7 }"""),
            ["operations"] = ParseArray("""
            [{ "op":"project", "fields":[
                {"name":"id","path":"id"},
                {"name":"name","path":"name"}
            ]}]
            """)
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"id":7,"name":null}""");
    }

    [Fact]
    public async Task filter_equals_ignores_items_with_missing_path()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "kind":"a" },{ "kind":"b" },{ "name":"x" }]"""),
            ["operations"] = ParseArray("""[{ "op":"filter_equals", "path":"kind", "value":"a" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"kind":"a"}]""");
    }

    [Fact]
    public async Task flatten_missing_target_path_no_ops_returns_unchanged()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "groups":[[1,2],[3]] }"""),
            ["operations"] = ParseArray("""[{ "op":"flatten", "path":"missing.path" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"groups":[[1,2],[3]]}""");
    }

    [Fact]
    public async Task slice_with_limit_larger_than_remaining_returns_available()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[1,2,3]"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":1, "limit":100 }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("[2,3]");
    }

    [Fact]
    public async Task sort_with_type_key_falls_back_to_json_string_comparison()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "val":[1] },{ "val":[2] }]"""),
            ["operations"] = ParseArray("""[{ "type":"sort", "path":"val", "direction":"asc" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"val":[1]},{"val":[2]}]""");
    }

    [Fact]
    public async Task json_null_literal_input_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = "null",
            ["operations"] = ParseArray("""[{ "op":"select", "path":"anything" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("input must be a JSON object, array, or JSON string");
    }

    // ----------------------------------------------------------- Additional input parsing

    [Fact]
    public async Task input_as_list_is_accepted()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = new List<object?> { 1, 2, 3 },
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":0, "limit":2 }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("[1,2]");
    }

    [Fact]
    public async Task input_as_json_element_object_is_accepted()
    {
        using var doc = JsonDocument.Parse("""{ "name":"test" }""");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = doc.RootElement.Clone(),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"name" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"test\"");
    }

    [Fact]
    public async Task input_as_json_element_array_is_accepted()
    {
        using var doc = JsonDocument.Parse("""[10,20,30]""");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = doc.RootElement.Clone(),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":1, "limit":2 }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("[20,30]");
    }

    [Fact]
    public async Task input_as_json_element_string_kind_returns_error()
    {
        using var doc = JsonDocument.Parse("\"hello\"");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = doc.RootElement.Clone(),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"a" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("input must be a JSON object, array, or JSON string");
    }

    // ----------------------------------------------------------- Sort additional paths

    [Fact]
    public async Task sort_with_null_items_orders_them_last()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "rank":2 },null,{ "rank":1 }]"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"rank", "direction":"asc" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"rank":1},{"rank":2},null]""");
    }

    [Fact]
    public async Task sort_comparing_strings_returns_lexicographic_order()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "name":"charlie" },{ "name":"alice" },{ "name":"bob" }]"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"name", "direction":"asc" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"name":"alice"},{"name":"bob"},{"name":"charlie"}]""");
    }

    [Fact]
    public async Task sort_comparing_booleans_returns_correct_order()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "active":true },{ "active":false },{ "active":true }]"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"active", "direction":"asc" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"active":false},{"active":true},{"active":true}]""");
    }

    [Fact]
    public async Task sort_comparing_mixed_types_uses_fallback_comparison()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[{ "val":true },{ "val":1 },{ "val":"apple" }]"""),
            ["operations"] = ParseArray("""[{ "op":"sort", "path":"val", "direction":"asc" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""[{"val":"apple"},{"val":1},{"val":true}]""");
    }

    // ----------------------------------------------------------- Project with nested path

    [Fact]
    public async Task project_with_nested_path_resolves_correctly()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "items":[{ "name":"first" },{ "name":"second" }] }"""),
            ["operations"] = ParseArray("""
            [{ "op":"project", "fields":[
                {"name":"first","path":"items[0].name"},
                {"name":"second","path":"items[1].name"}
            ]}]
            """)
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"first":"first","second":"second"}""");
    }

    // ----------------------------------------------------------- Flatten additional paths

    [Fact]
    public async Task flatten_non_array_value_at_path_returns_unchanged()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "data":"not an array" }"""),
            ["operations"] = ParseArray("""[{ "op":"flatten", "path":"data" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"data":"not an array"}""");
    }

    [Fact]
    public async Task flatten_mixed_array_returns_unchanged()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "groups":[[1,2],3,"text"] }"""),
            ["operations"] = ParseArray("""[{ "op":"flatten", "path":"groups" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("""{"groups":[[1,2],3,"text"]}""");
    }

    // ----------------------------------------------------------- Slice additional paths

    [Fact]
    public async Task slice_with_limit_zero_returns_empty()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[1,2,3]"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":0, "limit":0 }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("[]");
    }

    // ----------------------------------------------------------- Select mixed path

    [Fact]
    public async Task select_with_mixed_path_resolves_correctly()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "items":[{ "name":"first" },{ "name":"second" }] }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"items[0].name" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("\"first\"");
    }

    // ----------------------------------------------------------- Path parsing errors

    [Fact]
    public async Task path_with_whitespace_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"a b" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unsupported syntax");
    }

    [Fact]
    public async Task path_with_dollar_sign_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"$.a" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unsupported syntax");
    }

    [Fact]
    public async Task path_with_wildcard_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"items[*]" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unsupported syntax");
    }

    [Fact]
    public async Task path_with_double_dot_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"a..b" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unsupported syntax");
    }

    [Fact]
    public async Task empty_path_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"" }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("operation path is required");
    }

    [Fact]
    public async Task trailing_dot_path_returns_error()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseObject("""{ "a":1 }"""),
            ["operations"] = ParseArray("""[{ "op":"select", "path":"a." }]""")
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("path cannot end with '.'");
    }

    // ----------------------------------------------------------- TryReadInt indirect paths

    [Fact]
    public async Task slice_with_string_limit_parses_correctly()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["input"] = ParseArray("""[1,2,3,4,5]"""),
            ["operations"] = ParseArray("""[{ "op":"slice", "offset":0, "limit":"3" }]""")
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("[1,2,3]");
    }

    // ----------------------------------------------------------- Helpers

    private static async Task<ToolResult> InvokeAsync(IDictionary<string, object?> args, CancellationToken ct = default)
    {
        var tool = JsonTransformTool.Create(Mock.Of<IServiceScopeFactory>());
        return await tool.Handler!(args, ct);
    }

    private static JsonArray ParseArray(string json) => JsonNode.Parse(json)!.AsArray();
    private static JsonObject ParseObject(string json) => JsonNode.Parse(json)!.AsObject();
}
