using LeanKernel.Logic.Configuration;

namespace LeanKernel.Logic.Tools;

/// <summary>
/// Applies the tool allowlist governance policy from configuration.
/// Name allowlist takes precedence over category allowlist.
/// An empty allowlist means all tools in that dimension are permitted.
/// </summary>
public sealed class ToolGovernancePolicy
{
    private readonly ToolSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolGovernancePolicy"/> class.
    /// Initializes a new instance of <see cref="ToolGovernancePolicy"/>.
    /// </summary>
    public ToolGovernancePolicy(ToolSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Returns true when the given tool definition passes the governance policy.
    /// </summary>
    public bool IsAllowed(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        // Name allowlist takes precedence
        if (_settings.AllowedToolNames.Count > 0)
        {
            return _settings.AllowedToolNames.Any(n =>
                string.Equals(n, tool.Name, StringComparison.OrdinalIgnoreCase));
        }

        // Category allowlist when name list is empty
        if (_settings.AllowedCategories.Count > 0)
        {
            return _settings.AllowedCategories.Any(c =>
                string.Equals(c, tool.Category, StringComparison.OrdinalIgnoreCase));
        }

        // Neither gate applied — all tools pass
        return true;
    }

    /// <summary>
    /// Filters a list of tool definitions to only those permitted by the governance policy.
    /// </summary>
    public IEnumerable<ToolDefinition> Filter(IEnumerable<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return tools.Where(IsAllowed);
    }
}