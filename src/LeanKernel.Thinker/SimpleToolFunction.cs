using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Thinker;

/// <summary>
/// Exposes a simple <see cref="ITool"/> to the model using the tool's declared JSON schema.
/// </summary>
internal sealed class SimpleToolFunction : AIFunction
{
    private readonly ITool _tool;
    private readonly IToolExecutionAuthorizer? _executionAuthorizer;
    private readonly ILogger? _logger;
    private readonly JsonElement _schema;

    public SimpleToolFunction(
        ITool tool,
        IToolExecutionAuthorizer? executionAuthorizer,
        ILogger? logger = null)
    {
        _tool = tool;
        _executionAuthorizer = executionAuthorizer;
        _logger = logger;
        _schema = ParseSchema(tool.ParametersSchema);
    }

    public override string Name => _tool.Name;

    public override string Description => _tool.Description;

    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var parametersJson = JsonSerializer.Serialize(
            arguments?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

        if (_executionAuthorizer is not null)
        {
            var authorization = await _executionAuthorizer.AuthorizeAsync(_tool.Name, parametersJson, cancellationToken);
            if (!authorization.IsAuthorized)
            {
                _logger?.LogWarning(
                    "Tool execution denied for {Tool} (action: {Action}): {Reason}",
                    _tool.Name,
                    authorization.ActionType ?? "unknown",
                    authorization.Reason ?? "unauthorized");
                return $"Error: {authorization.Reason ?? "Tool execution denied"}";
            }
        }

        _logger?.LogInformation("Tool invoked: {Tool} with parameters: {Params}",
            _tool.Name, parametersJson);

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
            catch (JsonException)
            {
                // Fall through to empty object schema when a tool provides invalid metadata.
            }
        }

        using var fallback = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return fallback.RootElement.Clone();
    }
}
