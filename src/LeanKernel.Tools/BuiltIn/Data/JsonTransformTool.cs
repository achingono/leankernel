using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tools.BuiltIn.Data;

/// <summary>
/// Provides functionality for json transform tool.
/// </summary>
public static class JsonTransformTool
{
    private const string ToolName = "json_transform";
    private const int MaxOperations = 50;
    private const int MaxOutputCharacters = 200_000;

    /// <summary>
    /// Executes create.
    /// </summary>
    /// <param name="scopeFactory">The scope factory.</param>
    /// <returns>The operation result.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Apply deterministic transforms to JSON payloads",
            Category = "data",
            Parameters =
            [
                new ToolParameter { Name = "input", Type = "object", Description = "JSON input as a string/object/array", Required = true },
                new ToolParameter { Name = "operations", Type = "array", Description = "Ordered transform operations", Required = true }
            ],
            Handler = HandleAsync
        };
    }

    private static Task<ToolResult> HandleAsync(IDictionary<string, object?> args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!args.TryGetValue("input", out var inputValue) || inputValue is null)
        {
            return Task.FromResult(Fail("input is required"));
        }

        if (!TryReadInputNode(inputValue, out var current, out var inputError))
        {
            return Task.FromResult(Fail(inputError));
        }

        var operations = ToolArgumentReader.GetJsonArrayOrNull(args, "operations");
        if (operations is null)
        {
            return Task.FromResult(Fail("operations must be an array"));
        }

        if (operations.Count == 0)
        {
            return Task.FromResult(Fail("operations must contain at least one operation"));
        }

        if (operations.Count > MaxOperations)
        {
            return Task.FromResult(Fail($"operations exceeds maximum count of {MaxOperations}"));
        }

        for (var index = 0; index < operations.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            if (operations[index] is not JsonObject operationObject)
            {
                return Task.FromResult(Fail($"operation {index + 1} must be an object"));
            }

            if (!TryReadOperationName(operationObject, out var operationName))
            {
                return Task.FromResult(Fail($"operation {index + 1} must define 'op' or 'type'"));
            }

            if (!TryApplyOperation(current, operationName, operationObject, index + 1, out current, out var operationError))
            {
                return Task.FromResult(Fail(operationError));
            }
        }

        var output = current?.ToJsonString() ?? "null";
        if (output.Length > MaxOutputCharacters)
        {
            return Task.FromResult(Fail($"transformed output exceeds {MaxOutputCharacters} characters"));
        }

        return Task.FromResult(new ToolResult
        {
            ToolName = ToolName,
            Success = true,
            Output = output
        });
    }

    private static bool TryReadInputNode(object inputValue, out JsonNode? node, out string error)
    {
        error = string.Empty;
        node = null;

        try
        {
            node = inputValue switch
            {
                JsonNode jsonNode => jsonNode.DeepClone(),
                JsonElement element when element.ValueKind is JsonValueKind.Object or JsonValueKind.Array => JsonNode.Parse(element.GetRawText()),
                JsonElement _ => null,
                string text => JsonNode.Parse(text),
                IDictionary<string, object?> map => JsonSerializer.SerializeToNode(map),
                IEnumerable<object?> items => JsonSerializer.SerializeToNode(items),
                _ => JsonSerializer.SerializeToNode(inputValue)
            };
        }
        catch (JsonException ex)
        {
            error = $"input must be valid JSON: {ex.Message}";
            return false;
        }

        if (node is null)
        {
            error = "input must be a JSON object, array, or JSON string";
            return false;
        }

        return true;
    }

    private static bool TryReadOperationName(JsonObject operation, out string operationName)
    {
        operationName = string.Empty;

        foreach (var key in new[] { "op", "type" })
        {
            if (!operation.TryGetPropertyValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value is JsonValue jsonValue &&
                jsonValue.TryGetValue<string>(out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                operationName = name.Trim().ToLowerInvariant();
                return true;
            }
        }

        return false;
    }

    private static bool TryApplyOperation(JsonNode? current, string operationName, JsonObject operation, int operationNumber, out JsonNode? updated, out string error)
    {
        updated = current;
        error = string.Empty;

        return operationName switch
        {
            "select" => TryApplySelect(current, operation, out updated, out error),
            "project" => TryApplyProject(current, operation, out updated, out error),
            "filter_equals" => TryApplyFilterEquals(current, operation, out updated, out error),
            "sort" => TryApplySort(current, operation, out updated, out error),
            "slice" => TryApplySlice(current, operation, out updated, out error),
            "flatten" => TryApplyFlatten(current, operation, out updated, out error),
            _ => FailOperation($"operation {operationNumber} uses unsupported op '{operationName}'", out error)
        };
    }

    private static bool TryApplySelect(JsonNode? current, JsonObject operation, out JsonNode? updated, out string error)
    {
        updated = null;
        if (!TryReadPath(operation, out var path, out error))
        {
            return false;
        }

        if (current is null || !TryEvaluatePath(current, path, out var lookup))
        {
            updated = null;
            return true;
        }

        updated = CloneNode(lookup.Value);
        return true;
    }

    private static bool TryApplyProject(JsonNode? current, JsonObject operation, out JsonNode? updated, out string error)
    {
        updated = null;
        error = string.Empty;

        if (!operation.TryGetPropertyValue("fields", out var fieldsNode) || fieldsNode is not JsonArray fields)
        {
            error = "project operation requires a fields array";
            return false;
        }

        var output = new JsonObject();
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index] is not JsonObject field)
            {
                error = $"project field at index {index} must be an object";
                return false;
            }

            if (!TryReadStringProperty(field, "name", out var name))
            {
                error = $"project field at index {index} is missing name";
                return false;
            }

            if (!TryReadStringProperty(field, "path", out var pathText))
            {
                error = $"project field '{name}' is missing path";
                return false;
            }

            if (!TryParsePath(pathText, out var pathTokens, out error))
            {
                error = $"invalid path '{pathText}' in project field '{name}': {error}";
                return false;
            }

            if (current is not null && TryEvaluatePath(current, pathTokens, out var lookup))
            {
                output[name] = CloneNode(lookup.Value);
            }
            else
            {
                output[name] = null;
            }
        }

        updated = output;
        return true;
    }

    private static bool TryApplyFilterEquals(JsonNode? current, JsonObject operation, out JsonNode? updated, out string error)
    {
        updated = null;
        if (current is not JsonArray source)
        {
            error = "filter_equals requires current input to be an array";
            return false;
        }

        if (!TryReadPath(operation, out var path, out error))
        {
            return false;
        }

        if (!operation.TryGetPropertyValue("value", out var expected))
        {
            error = "filter_equals operation requires value";
            return false;
        }

        var filtered = new JsonArray();
        foreach (var item in source)
        {
            if (item is null || !TryEvaluatePath(item, path, out var lookup))
            {
                continue;
            }

            if (JsonNode.DeepEquals(lookup.Value, expected))
            {
                filtered.Add(CloneNode(item));
            }
        }

        updated = filtered;
        return true;
    }

    private static bool TryApplySort(JsonNode? current, JsonObject operation, out JsonNode? updated, out string error)
    {
        updated = null;
        error = string.Empty;
        if (current is not JsonArray source)
        {
            error = "sort requires current input to be an array";
            return false;
        }

        if (!TryReadPath(operation, out var path, out error))
        {
            return false;
        }

        var direction = "asc";
        if (operation.TryGetPropertyValue("direction", out var directionNode) && directionNode is not null)
        {
            if (directionNode is JsonValue directionValue &&
                directionValue.TryGetValue<string>(out var rawDirection) &&
                !string.IsNullOrWhiteSpace(rawDirection))
            {
                direction = rawDirection.Trim().ToLowerInvariant();
            }
        }

        if (direction is not ("asc" or "desc"))
        {
            error = "sort direction must be 'asc' or 'desc'";
            return false;
        }

        var keyedItems = source
            .Select((node, index) =>
            {
                JsonNode? lookupValue = null;
                var found = false;
                var lookup = PathLookup.Missing;
                if (node is not null && TryEvaluatePath(node, path, out lookup))
                {
                    found = true;
                    lookupValue = lookup.Value;
                }

                return new SortEntry(index, node, found, lookupValue);
            })
            .ToList();

        keyedItems.Sort((left, right) => CompareSortEntries(left, right, direction));

        var sorted = new JsonArray();
        foreach (var entry in keyedItems)
        {
            sorted.Add(CloneNode(entry.Node));
        }

        updated = sorted;
        return true;
    }

    private static bool TryApplySlice(JsonNode? current, JsonObject operation, out JsonNode? updated, out string error)
    {
        updated = null;
        error = string.Empty;
        if (current is not JsonArray source)
        {
            error = "slice requires current input to be an array";
            return false;
        }

        var offset = 0;
        if (operation.TryGetPropertyValue("offset", out var offsetNode) &&
            !TryReadInt(offsetNode, out offset))
        {
            error = "slice offset must be an integer";
            return false;
        }

        if (!operation.TryGetPropertyValue("limit", out var limitNode) || !TryReadInt(limitNode, out var limit))
        {
            error = "slice limit must be an integer";
            return false;
        }

        if (limit < 0)
        {
            error = "slice limit must be non-negative";
            return false;
        }

        offset = Math.Max(offset, 0);
        if (offset > source.Count)
        {
            offset = source.Count;
        }

        var count = Math.Min(limit, source.Count - offset);
        var sliced = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            sliced.Add(CloneNode(source[offset + i]));
        }

        updated = sliced;
        return true;
    }

    private static bool TryApplyFlatten(JsonNode? current, JsonObject operation, out JsonNode? updated, out string error)
    {
        updated = current;
        if (!TryReadPath(operation, out var path, out error))
        {
            return false;
        }

        if (current is null || !TryEvaluatePath(current, path, out var lookup))
        {
            return true;
        }

        if (lookup.Value is not JsonArray arrayValue)
        {
            return true;
        }

        if (arrayValue.Any(item => item is not JsonArray))
        {
            return true;
        }

        var flattened = new JsonArray();
        foreach (var nested in arrayValue.Cast<JsonArray>())
        {
            foreach (var child in nested)
            {
                flattened.Add(CloneNode(child));
            }
        }

        updated = ReplaceAtPath(current, path, flattened);
        return true;
    }

    private static JsonNode? ReplaceAtPath(JsonNode root, IReadOnlyList<PathSegment> path, JsonNode replacement)
    {
        if (path.Count == 0)
        {
            return replacement;
        }

        var parentPath = path.Take(path.Count - 1).ToArray();
        var leaf = path[^1];

        if (!TryEvaluatePath(root, parentPath, out var parentLookup))
        {
            return root;
        }

        if (leaf.Kind == PathSegmentKind.Property && parentLookup.Value is JsonObject parentObject)
        {
            parentObject[leaf.PropertyName!] = replacement;
            return root;
        }

        if (leaf.Kind == PathSegmentKind.Index &&
            parentLookup.Value is JsonArray parentArray &&
            leaf.Index >= 0 &&
            leaf.Index < parentArray.Count)
        {
            parentArray[leaf.Index] = replacement;
        }

        return root;
    }

    private static bool TryReadPath(JsonObject operation, out PathSegment[] path, out string error)
    {
        path = [];
        error = string.Empty;
        if (!TryReadStringProperty(operation, "path", out var pathText))
        {
            error = "operation path is required";
            return false;
        }

        if (!TryParsePath(pathText, out path, out error))
        {
            return false;
        }

        return true;
    }

    private static bool TryParsePath(string path, out PathSegment[] segments, out string error)
    {
        segments = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path must not be empty";
            return false;
        }

        if (path.Any(char.IsWhiteSpace) || path.Contains('$') || path.Contains('*') || path.Contains("..", StringComparison.Ordinal))
        {
            error = "path contains unsupported syntax";
            return false;
        }

        var parsed = new List<PathSegment>();
        var index = 0;
        while (index < path.Length)
        {
            if (!TryReadProperty(path, ref index, parsed, out error))
            {
                return false;
            }

            while (index < path.Length && path[index] == '[')
            {
                if (!TryReadIndex(path, ref index, parsed, out error))
                {
                    return false;
                }
            }

            if (index == path.Length)
            {
                break;
            }

            if (path[index] != '.')
            {
                error = $"unexpected character '{path[index]}'";
                return false;
            }

            index++;
            if (index == path.Length)
            {
                error = "path cannot end with '.'";
                return false;
            }
        }

        segments = [.. parsed];
        return true;
    }

    private static bool TryReadProperty(string path, ref int index, List<PathSegment> output, out string error)
    {
        error = string.Empty;
        var start = index;
        while (index < path.Length && IsPathPropertyCharacter(path[index]))
        {
            index++;
        }

        if (index == start)
        {
            error = "path segment must start with a property name";
            return false;
        }

        output.Add(PathSegment.Property(path[start..index]));
        return true;
    }

    private static bool TryReadIndex(string path, ref int index, List<PathSegment> output, out string error)
    {
        error = string.Empty;
        index++; // skip '['
        var numberStart = index;

        while (index < path.Length && char.IsDigit(path[index]))
        {
            index++;
        }

        if (numberStart == index)
        {
            error = "array index must be a non-negative integer";
            return false;
        }

        if (index >= path.Length || path[index] != ']')
        {
            error = "array index must end with ']'";
            return false;
        }

        var tokenText = path[numberStart..index];
        index++; // skip ']'

        if (!int.TryParse(tokenText, NumberStyles.None, CultureInfo.InvariantCulture, out var tokenIndex))
        {
            error = "array index is invalid";
            return false;
        }

        output.Add(PathSegment.ArrayIndex(tokenIndex));
        return true;
    }

    private static bool TryEvaluatePath(JsonNode root, IReadOnlyList<PathSegment> path, out PathLookup lookup)
    {
        JsonNode? current = root;
        for (var i = 0; i < path.Count; i++)
        {
            var segment = path[i];
            if (segment.Kind == PathSegmentKind.Property)
            {
                if (current is not JsonObject objectNode || !objectNode.TryGetPropertyValue(segment.PropertyName!, out current))
                {
                    lookup = PathLookup.Missing;
                    return false;
                }
            }
            else
            {
                if (current is not JsonArray arrayNode || segment.Index < 0 || segment.Index >= arrayNode.Count)
                {
                    lookup = PathLookup.Missing;
                    return false;
                }

                current = arrayNode[segment.Index];
            }
        }

        lookup = new PathLookup(true, current);
        return true;
    }

    private static int CompareSortEntries(SortEntry left, SortEntry right, string direction)
    {
        if (left.HasPath != right.HasPath)
        {
            return left.HasPath ? -1 : 1;
        }

        if (!left.HasPath)
        {
            return left.OriginalIndex.CompareTo(right.OriginalIndex);
        }

        var comparison = CompareJsonNodes(left.PathValue, right.PathValue);
        if (direction == "desc")
        {
            comparison *= -1;
        }

        return comparison != 0 ? comparison : left.OriginalIndex.CompareTo(right.OriginalIndex);
    }

    private static int CompareJsonNodes(JsonNode? left, JsonNode? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (TryGetString(left, out var leftString) && TryGetString(right, out var rightString))
        {
            return string.Compare(leftString, rightString, StringComparison.Ordinal);
        }

        if (TryGetBool(left, out var leftBool) && TryGetBool(right, out var rightBool))
        {
            return leftBool.CompareTo(rightBool);
        }

        return string.Compare(left.ToJsonString(), right.ToJsonString(), StringComparison.Ordinal);
    }

    private static bool TryGetNumber(JsonNode node, out decimal value)
    {
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<decimal>(out value))
            {
                return true;
            }

            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                value = (decimal)doubleValue;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetString(JsonNode node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var parsed) && parsed is not null)
        {
            value = parsed;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetBool(JsonNode node, out bool value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryReadStringProperty(JsonObject source, string name, out string value)
    {
        value = string.Empty;
        if (!source.TryGetPropertyValue(name, out var node) || node is null)
        {
            return false;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var parsed) && !string.IsNullOrWhiteSpace(parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryReadInt(JsonNode? node, out int value)
    {
        value = default;
        if (node is null)
        {
            return false;
        }

        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<int>(out value))
        {
            return true;
        }

        if (jsonValue.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)longValue;
            return true;
        }

        if (jsonValue.TryGetValue<string>(out var text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static bool IsPathPropertyCharacter(char value) => char.IsLetterOrDigit(value) || value is '_';

    private static JsonNode? CloneNode(JsonNode? node) => node?.DeepClone();

    private static bool FailOperation(string operationError, out string error)
    {
        error = operationError;
        return false;
    }

    private static ToolResult Fail(string error) => new()
    {
        ToolName = ToolName,
        Success = false,
        Error = error
    };

    private readonly record struct SortEntry(int OriginalIndex, JsonNode? Node, bool HasPath, JsonNode? PathValue);
    private readonly record struct PathLookup(bool Exists, JsonNode? Value)
    {
        public static PathLookup Missing => new(false, null);
    }

    private enum PathSegmentKind
    {
        Property,
        Index
    }

    private readonly record struct PathSegment(PathSegmentKind Kind, string? PropertyName, int Index)
    {
        /// <summary>
        /// Executes property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The operation result.</returns>
        public static PathSegment Property(string name) => new(PathSegmentKind.Property, name, -1);
        /// <summary>
        /// Executes array index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>The operation result.</returns>
        public static PathSegment ArrayIndex(int index) => new(PathSegmentKind.Index, null, index);
    }
}
