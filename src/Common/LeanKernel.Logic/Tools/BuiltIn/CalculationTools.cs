using System.Text.Json;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn;

/// <summary>
/// Provides deterministic local calculation and aggregation built-in tools:
/// <c>calculate</c>, <c>count</c>, <c>sum</c>, <c>average</c>, <c>min_max</c>, and <c>group_by</c>.
/// </summary>
public static class CalculationTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates all enabled calculation/aggregation tool definitions.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create a DI scope for reading settings.</param>
    /// <returns>The collection of tool definitions.</returns>
    public static IEnumerable<ToolDefinition> Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        return CreateIterator(scopeFactory);
    }

    private static IEnumerable<ToolDefinition> CreateIterator(IServiceScopeFactory scopeFactory)
    {
        yield return CreateCalculateTool();
        yield return CreateCountTool(scopeFactory);
        yield return CreateSumTool(scopeFactory);
        yield return CreateAverageTool(scopeFactory);
        yield return CreateMinMaxTool(scopeFactory);
        yield return CreateGroupByTool(scopeFactory);
    }

    private static ToolDefinition CreateCalculateTool() =>
        new()
        {
            Name = "calculate",
            Description = "Evaluates a simple arithmetic expression and returns the numeric result. Supports +, -, *, /, parentheses, and integer/decimal literals.",
            Category = "calculation",
            Parameters =
            [
                new ToolParameter
                {
                    Name = "expression",
                    Type = "string",
                    Description = "Arithmetic expression to evaluate, e.g. '(3 + 4) * 2'",
                    Required = true
                }
            ],
            Handler = (args, _) =>
            {
                var expr = ToolArgumentReader.GetString(args, "expression");
                if (string.IsNullOrWhiteSpace(expr))
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = "calculate",
                        Success = false,
                        Error = "expression is required"
                    });
                }

                try
                {
                    var result = ArithmeticEvaluator.Evaluate(expr);
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = "calculate",
                        Success = true,
                        Output = result.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                    });
                }
                catch (Exception ex)
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = "calculate",
                        Success = false,
                        Error = ex.Message
                    });
                }
            }
        };

    private static ToolDefinition CreateCountTool(IServiceScopeFactory scopeFactory) =>
        new()
        {
            Name = "count",
            Description = "Counts the number of items in a JSON array.",
            Category = "calculation",
            Parameters =
            [
                new ToolParameter
                {
                    Name = "items",
                    Type = "string",
                    Description = "JSON array of items to count",
                    Required = true
                }
            ],
            Handler = (args, _) =>
            {
                var items = ParseArray(ToolArgumentReader.GetJson(args, "items"), "count", out var error);
                if (error is not null)
                {
                    return Task.FromResult(error);
                }

                using var scope = scopeFactory.CreateScope();
                var maxItems = GetMaxInputItems(scope);

                if (items.Count > maxItems)
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = "count",
                        Success = false,
                        Error = $"Input exceeds maximum of {maxItems} items."
                    });
                }

                return Task.FromResult(new ToolResult
                {
                    ToolName = "count",
                    Success = true,
                    Output = items.Count.ToString()
                });
            }
        };

    private static ToolDefinition CreateSumTool(IServiceScopeFactory scopeFactory) =>
        new()
        {
            Name = "sum",
            Description = "Sums numeric values in a JSON array. Optionally sums a named field from an array of objects.",
            Category = "calculation",
            Parameters =
            [
                new ToolParameter { Name = "items", Type = "string", Description = "JSON array of numbers or objects", Required = true },
                new ToolParameter { Name = "field", Type = "string", Description = "When items is an array of objects, the numeric field name to sum", Required = false }
            ],
            Handler = (args, _) =>
            {
                var items = ParseArray(ToolArgumentReader.GetJson(args, "items"), "sum", out var error);
                if (error is not null)
                {
                    return Task.FromResult(error);
                }

                using var scope = scopeFactory.CreateScope();
                var maxItems = GetMaxInputItems(scope);
                if (items.Count > maxItems)
                {
                    return Task.FromResult(OverflowError("sum", maxItems));
                }

                var field = ToolArgumentReader.GetString(args, "field");
                double total = 0;
                foreach (var el in items)
                {
                    var v = ExtractNumber(el, field);
                    if (v is null)
                    {
                        continue;
                    }

                    total += v.Value;
                }

                return Task.FromResult(new ToolResult
                {
                    ToolName = "sum",
                    Success = true,
                    Output = total.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                });
            }
        };

    private static ToolDefinition CreateAverageTool(IServiceScopeFactory scopeFactory) =>
        new()
        {
            Name = "average",
            Description = "Computes the average of numeric values in a JSON array. Optionally averages a named field from an array of objects.",
            Category = "calculation",
            Parameters =
            [
                new ToolParameter { Name = "items", Type = "string", Description = "JSON array of numbers or objects", Required = true },
                new ToolParameter { Name = "field", Type = "string", Description = "Numeric field name to average when items contains objects", Required = false }
            ],
            Handler = (args, _) =>
            {
                var items = ParseArray(ToolArgumentReader.GetJson(args, "items"), "average", out var error);
                if (error is not null)
                {
                    return Task.FromResult(error);
                }

                using var scope = scopeFactory.CreateScope();
                var maxItems = GetMaxInputItems(scope);
                if (items.Count > maxItems)
                {
                    return Task.FromResult(OverflowError("average", maxItems));
                }

                var field = ToolArgumentReader.GetString(args, "field");
                var values = items
                    .Select(el => ExtractNumber(el, field))
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                if (values.Count == 0)
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = "average",
                        Success = false,
                        Error = "No numeric values found."
                    });
                }

                var avg = values.Average();
                return Task.FromResult(new ToolResult
                {
                    ToolName = "average",
                    Success = true,
                    Output = avg.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                });
            }
        };

    private static ToolDefinition CreateMinMaxTool(IServiceScopeFactory scopeFactory) =>
        new()
        {
            Name = "min_max",
            Description = "Finds the minimum and maximum numeric values in a JSON array. Optionally operates on a named field from an array of objects.",
            Category = "calculation",
            Parameters =
            [
                new ToolParameter { Name = "items", Type = "string", Description = "JSON array of numbers or objects", Required = true },
                new ToolParameter { Name = "field", Type = "string", Description = "Numeric field name when items contains objects", Required = false }
            ],
            Handler = (args, _) =>
            {
                var items = ParseArray(ToolArgumentReader.GetJson(args, "items"), "min_max", out var error);
                if (error is not null)
                {
                    return Task.FromResult(error);
                }

                using var scope = scopeFactory.CreateScope();
                var maxItems = GetMaxInputItems(scope);
                if (items.Count > maxItems)
                {
                    return Task.FromResult(OverflowError("min_max", maxItems));
                }

                var field = ToolArgumentReader.GetString(args, "field");
                var values = items
                    .Select(el => ExtractNumber(el, field))
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                if (values.Count == 0)
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = "min_max",
                        Success = false,
                        Error = "No numeric values found."
                    });
                }

                var output = JsonSerializer.Serialize(new
                {
                    min = values.Min(),
                    max = values.Max(),
                    count = values.Count
                }, JsonOptions);

                return Task.FromResult(new ToolResult
                {
                    ToolName = "min_max",
                    Success = true,
                    Output = output
                });
            }
        };

    private static ToolDefinition CreateGroupByTool(IServiceScopeFactory scopeFactory) =>
        new()
        {
            Name = "group_by",
            Description = "Groups items in a JSON array of objects by a specified key field, returning group counts.",
            Category = "calculation",
            Parameters =
            [
                new ToolParameter { Name = "items", Type = "string", Description = "JSON array of objects to group", Required = true },
                new ToolParameter { Name = "key",   Type = "string", Description = "The field name to group by",     Required = true }
            ],
            Handler = (args, _) => Task.FromResult(ExecuteGroupBy(args, scopeFactory))
        };

    private static ToolResult ExecuteGroupBy(IReadOnlyDictionary<string, object?> args, IServiceScopeFactory scopeFactory)
    {
        var items = ParseArray(ToolArgumentReader.GetJson(args, "items"), "group_by", out var error);
        if (error is not null)
        {
            return error;
        }

        var groupKey = ToolArgumentReader.GetString(args, "key");
        if (string.IsNullOrWhiteSpace(groupKey))
        {
            return new ToolResult { ToolName = "group_by", Success = false, Error = "key is required" };
        }

        using var scope = scopeFactory.CreateScope();
        var maxItems = GetMaxInputItems(scope);
        if (items.Count > maxItems)
        {
            return OverflowError("group_by", maxItems);
        }

        var groups = BuildGroupCounts(items, groupKey);
        return new ToolResult
        {
            ToolName = "group_by",
            Success = true,
            Output = JsonSerializer.Serialize(groups, JsonOptions)
        };
    }

    private static Dictionary<string, int> BuildGroupCounts(List<JsonElement> items, string groupKey)
    {
        var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in items)
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(groupKey, out var prop))
            {
                continue;
            }

            var keyVal = prop.ValueKind == JsonValueKind.String
                ? prop.GetString() ?? "(null)"
                : prop.GetRawText();

            groups[keyVal] = groups.TryGetValue(keyVal, out var c) ? c + 1 : 1;
        }

        return groups;
    }

    private static List<JsonElement> ParseArray(string? json, string toolName, out ToolResult? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = new ToolResult { ToolName = toolName, Success = false, Error = "items is required" };
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = new ToolResult { ToolName = toolName, Success = false, Error = "items must be a JSON array" };
                return [];
            }

            return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
        }
        catch (JsonException ex)
        {
            error = new ToolResult { ToolName = toolName, Success = false, Error = $"Invalid JSON: {ex.Message}" };
            return [];
        }
    }

    private static double? ExtractNumber(JsonElement el, string? field)
    {
        if (!string.IsNullOrWhiteSpace(field) && el.ValueKind == JsonValueKind.Object && !el.TryGetProperty(field, out el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out var d) => d,
            _ => null
        };
    }

    private static int GetMaxInputItems(IServiceScope scope)
    {
        try
        {
            return scope.ServiceProvider
                .GetRequiredService<IOptions<AgentSettings>>().Value
                .Tools.BuiltIns.Calculation.MaxInputItems;
        }
        catch
        {
            return 1000;
        }
    }

    private static ToolResult OverflowError(string toolName, int maxItems) =>
        new() { ToolName = toolName, Success = false, Error = $"Input exceeds maximum of {maxItems} items." };
}
