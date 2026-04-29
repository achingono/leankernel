using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Thinker;

/// <summary>
/// Bridges LeanKernel's framework-agnostic <see cref="ITool"/> / <see cref="IToolRegistry"/>
/// to MAF's <see cref="AITool"/> via <see cref="AIFunctionFactory"/>.
/// Replaces <c>ToolFunctionBinder</c> (Semantic Kernel).
/// </summary>
public sealed class ToolFunctionAdapter
{
    private readonly IToolRegistry _registry;
    private readonly ILogger<ToolFunctionAdapter> _logger;

    public ToolFunctionAdapter(IToolRegistry registry, ILogger<ToolFunctionAdapter> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Convert all registered <see cref="ITool"/> instances to MAF <see cref="AITool"/> list.
    /// Each tool is exposed as a function with a single <c>input</c> string parameter
    /// (matching the existing ToolFunctionBinder contract).
    /// </summary>
    public IReadOnlyList<AITool> BuildTools()
    {
        var tools = _registry.Tools.Values
            .Select(tool =>
            {
                var capturedTool = tool;
                return AIFunctionFactory.Create(
                    async (string input, CancellationToken ct) =>
                    {
                        _logger.LogInformation("Tool invoked: {Tool} with input: {Input}",
                            capturedTool.Name, input);
                        var result = await capturedTool.ExecuteAsync(input, ct);
                        return result.Success ? result.Output ?? "" : $"Error: {result.Error}";
                    },
                    name: capturedTool.Name,
                    description: capturedTool.Description);
            })
            .Cast<AITool>()
            .ToList();

        _logger.LogInformation("Built {Count} AI tools from registry", tools.Count);
        return tools;
    }
}
