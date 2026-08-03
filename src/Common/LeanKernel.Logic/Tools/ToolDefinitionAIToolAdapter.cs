using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.Tools;

/// <summary>
/// Adapts a LeanKernel <see cref="ToolDefinition"/> to an <see cref="AITool"/> consumable
/// by the Microsoft.Extensions.AI function-invocation pipeline.
/// </summary>
public static class ToolDefinitionAIToolAdapter
{
    /// <summary>
    /// Converts a <see cref="ToolDefinition"/> to an <see cref="AITool"/>.
    /// </summary>
    /// <param name="tool">The tool definition to adapt.</param>
    /// <returns>The adapted <see cref="AITool"/>.</returns>
    public static AITool ToAITool(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return new ToolDefinitionAIFunction(tool);
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

    private sealed class ToolDefinitionAIFunction(ToolDefinition tool) : AIFunction
    {
        private readonly ToolDefinition _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        private readonly JsonElement _jsonSchema = BuildJsonSchema(tool.Parameters);

        public override string Name => _tool.Name;

        public override string Description => BuildDescription(_tool);

        public override JsonElement JsonSchema => _jsonSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            if (_tool.Handler is null)
            {
                return $"Tool '{_tool.Name}' has no execution handler";
            }

            var result = await _tool.Handler(
                new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                return result.Output ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(result.Error)
                ? $"Tool '{_tool.Name}' failed."
                : result.Error;
        }
    }

    private static string BuildDescription(ToolDefinition tool)
    {
        if (tool.Parameters is not { Count: > 0 })
        {
            return tool.Description;
        }

        var builder = new StringBuilder(tool.Description);
        builder.Append(" Parameters: ");
        builder.AppendJoin(
            "; ",
            tool.Parameters.Select(parameter =>
            {
                var requiredSuffix = parameter.Required ? "required" : "optional";
                var description = string.IsNullOrWhiteSpace(parameter.Description)
                    ? string.Empty
                    : $" - {parameter.Description}";
                return $"{parameter.Name} ({NormalizeSchemaType(parameter.Type)}, {requiredSuffix}){description}";
            }));
        return builder.ToString();
    }

    private static JsonElement BuildJsonSchema(IReadOnlyList<ToolParameter>? parameters)
    {
        JsonObject properties = new();
        JsonArray required = [];

        foreach (var parameter in parameters ?? [])
        {
            JsonObject parameterSchema = new()
            {
                ["type"] = NormalizeSchemaType(parameter.Type)
            };

            if (!string.IsNullOrWhiteSpace(parameter.Description))
            {
                parameterSchema["description"] = parameter.Description;
            }

            properties[parameter.Name] = parameterSchema;

            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        JsonObject schema = new()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.SerializeToElement(schema);
    }

    private static string NormalizeSchemaType(string? declaredType)
        => declaredType?.Trim().ToLowerInvariant() switch
        {
            "array" => "array",
            "boolean" => "boolean",
            "integer" => "integer",
            "number" => "number",
            "object" => "object",
            _ => "string"
        };
}
