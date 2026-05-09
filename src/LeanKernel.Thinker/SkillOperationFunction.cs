using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Thinker;

/// <summary>
/// A custom <see cref="AIFunction"/> that exposes a single skill operation to the LLM with
/// its full per-field JSON Schema, so the LLM receives accurate parameter names and types
/// rather than an opaque <c>string parameters</c> argument.
/// </summary>
internal sealed class SkillOperationFunction : AIFunction
{
    private readonly IOperationsTool _tool;
    private readonly ToolOperationDescriptor _operation;
    private readonly IToolExecutionAuthorizer? _executionAuthorizer;
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _schema;
    private readonly ILogger? _logger;

    public SkillOperationFunction(
        IOperationsTool tool,
        ToolOperationDescriptor operation,
        IToolExecutionAuthorizer? executionAuthorizer,
        ILogger? logger = null)
    {
        _tool = tool;
        _operation = operation;
        _executionAuthorizer = executionAuthorizer;
        _name = $"{tool.Name}__{operation.Id}";
        _description = $"{operation.Summary} (skill: {tool.Name})";
        _logger = logger;
        _schema = ParseSchema(operation.ParametersSchema);
    }

    public override string Name => _name;
    public override string Description => _description;
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // Build the JSON payload: merge LLM-supplied named args with the operation id.
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["operation"] = _operation.Id
        };

        if (arguments != null)
        {
            foreach (var kv in arguments)
                dict[kv.Key] = kv.Value;
        }

        var parametersJson = JsonSerializer.Serialize(dict);

        if (_executionAuthorizer is not null)
        {
            var authorization = await _executionAuthorizer.AuthorizeAsync(_name, parametersJson, cancellationToken);
            if (!authorization.IsAuthorized)
            {
                _logger?.LogWarning(
                    "Tool execution denied for {Tool}.{Op} (action: {Action}): {Reason}",
                    _tool.Name,
                    _operation.Id,
                    authorization.ActionType ?? "unknown",
                    authorization.Reason ?? "unauthorized");
                return $"Error: {authorization.Reason ?? "Tool execution denied"}";
            }
        }

        _logger?.LogInformation("Tool invoked: {Tool}.{Op} with parameters: {Params}",
            _tool.Name, _operation.Id, parametersJson);

        var result = await _tool.ExecuteAsync(parametersJson, cancellationToken);
        return result.Success ? result.Output ?? "" : $"Error: {result.Error}";
    }

    private static JsonElement ParseSchema(string? schemaJson)
    {
        if (!string.IsNullOrEmpty(schemaJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(schemaJson);
                return doc.RootElement.Clone();
            }
            catch { /* fall through to empty schema */ }
        }

        using var fallback = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return fallback.RootElement.Clone();
    }
}
