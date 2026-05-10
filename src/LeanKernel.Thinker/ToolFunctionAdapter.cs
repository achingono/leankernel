using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Thinker.Agents;

namespace LeanKernel.Thinker;

/// <summary>
/// Bridges LeanKernel's framework-agnostic <see cref="ITool"/> / <see cref="IToolRegistry"/>
/// to MAF's <see cref="AITool"/> via <see cref="AIFunctionFactory"/>.
/// Replaces the former SK-based ToolFunctionBinder.
/// </summary>
public sealed class ToolFunctionAdapter
{
    private readonly IToolRegistry _registry;
    private readonly ILogger<ToolFunctionAdapter> _logger;
    private readonly IToolExecutionAuthorizer? _executionAuthorizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolFunctionAdapter" /> class.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="logger">The logger.</param>
    public ToolFunctionAdapter(IToolRegistry registry, ILogger<ToolFunctionAdapter> logger)
        : this(registry, logger, null)
    {
    }

    /// <summary>
    /// Represents the tool function adapter.
    /// </summary>
    public ToolFunctionAdapter(
        IToolRegistry registry,
        ILogger<ToolFunctionAdapter> logger,
        IToolExecutionAuthorizer? executionAuthorizer)
    {
        _registry = registry;
        _logger = logger;
        _executionAuthorizer = executionAuthorizer;
    }

    /// <summary>
    /// Convert all registered <see cref="ITool"/> instances to MAF <see cref="AITool"/> list.
    /// Each tool is exposed as a function with a single <c>input</c> string parameter
    /// (matching the existing ToolFunctionBinder contract).
    /// </summary>
    public IReadOnlyList<AITool> BuildTools()
    {
        return BuildToolsForAgent(null);
    }

    /// <summary>
    /// Build tools for a specific agent, enforcing AllowedTools and AllowedCategories constraints.
    /// </summary>
    /// <param name="agent">Agent definition containing constraints. If null, returns all tools.</param>
    /// <returns>Filtered list of tools the agent is allowed to use.</returns>
    public IReadOnlyList<AITool> BuildToolsForAgent(AgentDefinition? agent)
    {
        var tools = _registry.Tools.Values
            .Where(tool => IsToolAllowedForAgent(tool, agent))
            .SelectMany(tool => BuildFunctionsForTool(tool))
            .Cast<AITool>()
            .ToList();

        _logger.LogInformation("Built {Count} AI tools{Agent} from registry", 
            tools.Count, 
            agent != null ? $" for agent '{agent.Name}'" : "");
        
        return tools;
    }

    /// <summary>
    /// Expand a tool into one AIFunction per operation (for IOperationsTool) or a single
    /// function with a JSON input string (for simple tools).
    /// </summary>
    private IEnumerable<AIFunction> BuildFunctionsForTool(ITool tool)
    {
        if (tool is IOperationsTool multiOp && multiOp.Operations.Count > 0)
        {
            foreach (var op in multiOp.Operations)
            {
                yield return new SkillOperationFunction(multiOp, op, _executionAuthorizer, _logger);
            }
        }
        else
        {
            var capturedTool = tool;
            var authorizer = _executionAuthorizer;
            yield return AIFunctionFactory.Create(
                async (string input, CancellationToken ct) =>
                {
                    if (authorizer is not null)
                    {
                        var authorization = await authorizer.AuthorizeAsync(capturedTool.Name, input, ct);
                        if (!authorization.IsAuthorized)
                        {
                            _logger.LogWarning(
                                "Tool execution denied for {Tool} (action: {Action}): {Reason}",
                                capturedTool.Name,
                                authorization.ActionType ?? "unknown",
                                authorization.Reason ?? "unauthorized");
                            return $"Error: {authorization.Reason ?? "Tool execution denied"}";
                        }
                    }

                    _logger.LogInformation("Tool invoked: {Tool} with input: {Input}",
                        capturedTool.Name, input);
                    var result = await capturedTool.ExecuteAsync(input, ct);
                    return result.Success ? result.Output ?? "" : $"Error: {result.Error}";
                },
                name: capturedTool.Name,
                description: capturedTool.Description);
        }
    }

    /// <summary>
    /// Check if a tool is allowed for an agent based on AllowedTools and AllowedCategories.
    /// </summary>
    private bool IsToolAllowedForAgent(ITool tool, AgentDefinition? agent)
    {
        if (agent == null)
            return true; // No agent constraint, allow all tools

        // Check explicit tool name allowlist
        if (agent.AllowedTools.Count > 0 && agent.AllowedTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
            return true;

        // Check category-based allowlist
        if (agent.AllowedCategories.Count > 0 && agent.AllowedCategories.Contains(tool.Category, StringComparer.OrdinalIgnoreCase))
            return true;

        // If either list is defined and tool doesn't match, disallow
        if (agent.AllowedTools.Count > 0 || agent.AllowedCategories.Count > 0)
            return false;

        // If no constraints defined, allow
        return true;
    }
}
