using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Thinker.SemanticKernel;

/// <summary>
/// Binds ITool implementations to Semantic Kernel KernelFunctions,
/// enabling the LLM to invoke tools during reasoning.
/// </summary>
public sealed class ToolFunctionBinder
{
    private readonly IToolRegistry _registry;
    private readonly ILogger<ToolFunctionBinder> _logger;

    public ToolFunctionBinder(IToolRegistry registry, ILogger<ToolFunctionBinder> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Register all tools from the registry as SK KernelFunctions on the given kernel.
    /// </summary>
    public void BindAll(Kernel kernel)
    {
        foreach (var (name, tool) in _registry.Tools)
        {
            var capturedTool = tool;
            var function = KernelFunctionFactory.CreateFromMethod(
                async (string input, CancellationToken ct) =>
                {
                    _logger.LogInformation("Tool invoked: {Tool} with input: {Input}", capturedTool.Name, input);
                    var result = await capturedTool.ExecuteAsync(input, ct);
                    return result.Success ? result.Output : $"Error: {result.Error}";
                },
                functionName: capturedTool.Name,
                description: capturedTool.Description);

            kernel.Plugins.AddFromFunctions(capturedTool.Name, [function]);
        }

        _logger.LogInformation("Bound {Count} tools to Semantic Kernel", _registry.Tools.Count);
    }
}
