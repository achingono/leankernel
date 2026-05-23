using LeanKernel.Abstractions.Models;

namespace LeanKernel.Tools;

/// <summary>
/// Determines tool visibility from the caller-provided allow lists.
/// Explicit tool-name allow lists take precedence over category filters.
/// When no allow list is provided, all registered tools remain visible.
/// </summary>
public sealed class ToolGovernancePolicy
{
    /// <summary>
    /// Returns true when the tool is visible in the given context.
    /// </summary>
    public bool IsVisible(ToolDefinition tool, ToolVisibilityContext context)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(context);

        if (context.AllowedToolNames is { Count: > 0 })
        {
            return context.AllowedToolNames.Contains(tool.Name, StringComparer.OrdinalIgnoreCase);
        }

        if (context.AllowedCategories is { Count: > 0 })
        {
            return tool.Category is not null &&
                context.AllowedCategories.Contains(tool.Category, StringComparer.OrdinalIgnoreCase);
        }

        return true;
    }
}
