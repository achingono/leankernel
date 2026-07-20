using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Logic.Tools.BuiltIn.Data;

/// <summary>
/// Applies deterministic JSON transforms (select, project, filter_equals, sort, slice, flatten).
/// </summary>
public static class JsonTransformTool
{
    private const string ToolName = "json_transform";
    private const int MaxOperations = 50;
    private const int MaxOutputCharacters = 200_000;

    /// <summary>
    /// Creates a tool definition for applying JSON transforms.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>A <see cref="ToolDefinition"/> for the JSON transform tool.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Apply deterministic JSON transforms: select, project, filter_equals, sort, slice, flatten",
            Category = "data",
            Parameters =
            [
                new ToolParameter { Name = "input", Type = "object", Description = "Input JSON data", Required = true },
                new ToolParameter { Name = "operations", Type = "object", Description = "Array of transform operations", Required = true }
            ],
            Handler = async (args, ct) =>
            {
                await Task.CompletedTask;
                return Handle(args);
            }
        };
    }

    private static ToolResult Handle(IReadOnlyDictionary<string, object?> args)
    {
        var inputRaw = ToolArgumentReader.GetJson(args, "input");
        var operationsRaw = ToolArgumentReader.GetJson(args, "operations");

        if (string.IsNullOrWhiteSpace(inputRaw))
        {
            return Fail("Input is required");
        }

        if (string.IsNullOrWhiteSpace(operationsRaw))
        {
            return Fail("Operations are required");
        }

        JsonNode? current;
        try
        {
            current = JsonNode.Parse(inputRaw);
        }
        catch (JsonException ex)
        {
            return Fail($"Invalid input JSON: {ex.Message}");
        }

        JsonArray operations;
        try
        {
            operations = JsonNode.Parse(operationsRaw) as JsonArray ?? new JsonArray();
        }
        catch (JsonException ex)
        {
            return Fail($"Invalid operations JSON: {ex.Message}");
        }

        if (operations.Count > MaxOperations)
        {
            return Fail($"Too many operations. Maximum is {MaxOperations}.");
        }

        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i] as JsonObject;
            if (op is null)
            {
                return Fail($"Operation {i + 1} must be a JSON object");
            }

            if (!op.TryGetPropertyValue("op", out var opNameNode) || opNameNode is not JsonValue opNameVal)
            {
                return Fail($"Operation {i + 1} is missing 'op' field");
            }

            var opName = opNameVal.ToString();
            if (!TryApplyOperation(current, opName, op, out var updated, out var error))
            {
                return Fail($"Operation {i + 1} ({opName}) failed: {error}");
            }

            current = updated;
        }

        var output = current?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        if (output.Length > MaxOutputCharacters)
        {
            output = output[..MaxOutputCharacters] + "\n\n[Output truncated]";
        }

        return new ToolResult { ToolName = ToolName, Success = true, Output = output };
    }

    private static bool TryApplyOperation(JsonNode? current, string opName, JsonObject op, out JsonNode? updated, out string? error)
    {
        error = null;
        updated = current;
        return opName switch
        {
            "select" => TryApplySelect(current, op, out updated, out error),
            "project" => TryApplyProject(current, op, out updated, out error),
            "filter_equals" => TryApplyFilterEquals(current, op, out updated, out error),
            "sort" => TryApplySort(current, op, out updated, out error),
            "slice" => TryApplySlice(current, op, out updated, out error),
            "flatten" => TryApplyFlatten(current, op, out updated, out error),
            _ => FailOp($"Unknown operation '{opName}'. Supported: select, project, filter_equals, sort, slice, flatten", out error)
        };
    }

    private static bool TryApplySelect(JsonNode? current, JsonObject op, out JsonNode? updated, out string? error)
    {
        error = null;
        if (!TryReadStringProperty(op, "path", out var path))
        {
            error = "Missing 'path'";
            updated = null;
            return false;
        }
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var node = current;
        foreach (var segment in segments)
        {
            if (node is JsonObject obj)
            {
                node = obj[segment];
            }
            else if (node is JsonArray arr && int.TryParse(segment, out var idx))
            {
                node = idx < arr.Count ? arr[idx] : null;
            }
            else
            {
                node = null;
                break;
            }
        }

        updated = node;
        return true;
    }

    private static bool TryApplyProject(JsonNode? current, JsonObject op, out JsonNode? updated, out string? error)
    {
        error = null;
        updated = current;
        if (current is not JsonArray arr)
        {
            error = "Input must be an array for project";
            return false;
        }
        if (!op.TryGetPropertyValue("fields", out var fieldsNode) || fieldsNode is not JsonArray fieldsArr)
        {
            error = "Missing 'fields' array";
            return false;
        }
        var fields = fieldsArr.Select(f => f?.ToString() ?? string.Empty).ToList();
        var result = new JsonArray();
        foreach (var item in arr)
        {
            if (item is JsonObject obj)
            {
                var projected = new JsonObject();
                foreach (var field in fields)
                {
                    if (obj.TryGetPropertyValue(field, out var val))
                    {
                        projected[field] = val?.DeepClone();
                    }
                }

                result.Add(projected);
            }
        }

        updated = result;
        return true;
    }

    private static bool TryApplyFilterEquals(JsonNode? current, JsonObject op, out JsonNode? updated, out string? error)
    {
        error = null;
        updated = current;
        if (current is not JsonArray arr)
        {
            error = "Input must be an array for filter_equals";
            return false;
        }
        if (!TryReadStringProperty(op, "field", out var field))
        {
            error = "Missing 'field'";
            return false;
        }
        if (!op.TryGetPropertyValue("value", out var filterValue))
        {
            error = "Missing 'value'";
            return false;
        }
        var result = new JsonArray();
        foreach (var item in arr)
        {
            if (item is JsonObject obj && obj.TryGetPropertyValue(field, out var val) && JsonNode.DeepEquals(val, filterValue))
            {
                result.Add(item?.DeepClone());
            }
        }

        updated = result;
        return true;
    }

    private static bool TryApplySort(JsonNode? current, JsonObject op, out JsonNode? updated, out string? error)
    {
        error = null;
        updated = current;
        if (current is not JsonArray arr)
        {
            error = "Input must be an array for sort";
            return false;
        }
        if (!TryReadStringProperty(op, "field", out var field))
        {
            error = "Missing 'field'";
            return false;
        }
        var descending = op.TryGetPropertyValue("descending", out var descNode) && descNode is JsonValue descVal && descVal.GetValue<bool>();
        var sorted = arr.Select((item, index) => (Item: item, Index: index, Value: item is JsonObject obj && obj.TryGetPropertyValue(field, out var v) ? v : null))
            .OrderBy(x => x.Value, JsonNodeComparer.Instance).ThenBy(x => x.Index).ToList();
        if (descending)
        {
            sorted = sorted.OrderByDescending(x => x.Value, JsonNodeComparer.Instance).ThenBy(x => x.Index).ToList();
        }

        var result = new JsonArray();
        foreach (var item in sorted)
        {
            result.Add(item.Item?.DeepClone());
        }

        updated = result;
        return true;
    }

    private static bool TryApplySlice(JsonNode? current, JsonObject op, out JsonNode? updated, out string? error)
    {
        error = null;
        updated = current;
        if (current is not JsonArray arr)
        {
            error = "Input must be an array for slice";
            return false;
        }
        var start = op.TryGetPropertyValue("start", out var sn) && sn is JsonValue sv ? sv.GetValue<int>() : 0;
        var end = op.TryGetPropertyValue("end", out var en) && en is JsonValue ev ? ev.GetValue<int>() : arr.Count;
        start = Math.Max(0, Math.Min(start, arr.Count));
        end = Math.Max(start, Math.Min(end, arr.Count));
        var result = new JsonArray();
        for (var i = start; i < end; i++)
        {
            result.Add(arr[i]?.DeepClone());
        }

        updated = result;
        return true;
    }

    private static bool TryApplyFlatten(JsonNode? current, JsonObject op, out JsonNode? updated, out string? error)
    {
        error = null;
        updated = current;
        if (current is not JsonArray arr)
        {
            error = "Input must be an array for flatten";
            return false;
        }
        var depth = op.TryGetPropertyValue("depth", out var dn) && dn is JsonValue dv ? dv.GetValue<int>() : 1;
        var result = new JsonArray();
        FlattenArray(arr, result, depth);
        updated = result;
        return true;
    }

    private static void FlattenArray(JsonArray source, JsonArray target, int depth)
    {
        foreach (var item in source)
        {
            if (item is JsonArray nested && depth > 0)
            {
                FlattenArray(nested, target, depth - 1);
            }
            else
            {
                target.Add(item?.DeepClone());
            }
        }
    }

    private static bool TryReadStringProperty(JsonObject obj, string name, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetPropertyValue(name, out var node) || node is not JsonValue val)
        {
            return false;
        }

        value = val.ToString();
        return true;
    }

    private static bool FailOp(string msg, out string error)
    {
        error = msg;
        return false;
    }
    private static ToolResult Fail(string error) => new() { ToolName = ToolName, Success = false, Error = error };

    private sealed class JsonNodeComparer : IComparer<JsonNode?>
    {
        public static readonly JsonNodeComparer Instance = new();
        public int Compare(JsonNode? x, JsonNode? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            if (x is JsonValue xv && y is JsonValue yv)
            {
                if (xv.TryGetValue(out double xd) && yv.TryGetValue(out double yd))
                {
                    return xd.CompareTo(yd);
                }

                if (xv.TryGetValue(out string? xs) && yv.TryGetValue(out string? ys))
                {
                    return string.Compare(xs, ys, StringComparison.Ordinal);
                }

                return string.Compare(xv.ToString(), yv.ToString(), StringComparison.Ordinal);
            }

            return string.Compare(x.ToJsonString(), y.ToJsonString(), StringComparison.Ordinal);
        }
    }
}