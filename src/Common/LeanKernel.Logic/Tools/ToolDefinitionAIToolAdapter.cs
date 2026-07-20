using System.Text.Json;

using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.Tools;

/// <summary>
/// Adapts a LeanKernel <see cref="ToolDefinition"/> to an <see cref="AITool"/> consumable
/// by the Microsoft.Extensions.AI function-invocation pipeline.
/// </summary>
public static class ToolDefinitionAIToolAdapter
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Converts a <see cref="ToolDefinition"/> to an <see cref="AITool"/>.
    /// </summary>
    /// <param name="tool">The tool definition to adapt.</param>
    /// <returns>The adapted <see cref="AITool"/>.</returns>
    public static AITool ToAITool(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return AIFunctionFactory.Create(
            BuildDelegate(tool),
            tool.Name,
            tool.Description);
    }

    /// <summary>
    /// Converts multiple <see cref="ToolDefinition"/> instances to <see cref="AITool"/> instances.
    /// </summary>
    /// <param name="tools">The tool definitions to adapt.</param>
    /// <returns>The adapted tools.</returns>
    public static IEnumerable<AITool> ToAITools(IEnumerable<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return tools.Select(ToAITool);
    }

    private static Func<string, CancellationToken, Task<string>> BuildDelegate(ToolDefinition tool)
    {
        return async (argsJson, ct) =>
        {
            IReadOnlyDictionary<string, object?> args;
            try
            {
                args = string.IsNullOrWhiteSpace(argsJson)
                    ? new Dictionary<string, object?>()
                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson, JsonOptions)
                      ?? new Dictionary<string, object?>();
            }
            catch (JsonException)
            {
                args = new Dictionary<string, object?>();
            }

            var result = await tool.Handler(args, ct).ConfigureAwait(false);
            return result.ToString();
        };
    }
}