using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Tools;

/// <summary>
/// Executes tools by name using registered handlers.
/// </summary>
public sealed class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry _registry;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(IToolRegistry registry, ILogger<ToolExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        var tool = _registry.GetTool(toolName);
        if (tool is null)
        {
            _logger.LogWarning("Tool not found: {ToolName}", toolName);
            return new ToolResult
            {
                ToolName = toolName,
                Success = false,
                Error = $"Tool '{toolName}' not found"
            };
        }

        if (tool.Handler is null)
        {
            _logger.LogWarning("Tool has no handler: {ToolName}", toolName);
            return new ToolResult
            {
                ToolName = toolName,
                Success = false,
                Error = $"Tool '{toolName}' has no execution handler"
            };
        }

        try
        {
            _logger.LogDebug("Executing tool: {ToolName}", toolName);
            var result = await tool.Handler(arguments, ct);
            _logger.LogDebug("Tool {ToolName} completed: success={Success}", toolName, result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", toolName);
            return new ToolResult
            {
                ToolName = toolName,
                Success = false,
                Error = ex.Message
            };
        }
    }
}
