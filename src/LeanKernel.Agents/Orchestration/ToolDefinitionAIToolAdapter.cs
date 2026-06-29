using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.AI;

namespace LeanKernel.Agents.Orchestration;

/// <summary>
/// Provides functionality for tool definition aitool adapter.
/// </summary>
internal static class ToolDefinitionAIToolAdapter
{
    /// <summary>
    /// Executes create.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <returns>The operation result.</returns>
    public static AITool Create(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return new ToolDefinitionAIFunction(tool);
    }

    private sealed class ToolDefinitionAIFunction(ToolDefinition tool) : AIFunction
    {
        private readonly ToolDefinition _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        private readonly JsonElement _jsonSchema = BuildJsonSchema(tool.Parameters);

        /// <summary>
        /// Gets name.
        /// </summary>
        public override string Name => _tool.Name;

        public override string Description => BuildDescription(_tool);

        /// <summary>
        /// Gets json schema.
        /// </summary>
        public override JsonElement JsonSchema => _jsonSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
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
        JsonArray required = new();

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
