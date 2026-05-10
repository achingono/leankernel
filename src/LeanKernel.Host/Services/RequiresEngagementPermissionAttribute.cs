namespace LeanKernel.Host.Services;

/// <summary>
/// Marks a controller action or service method as requiring engagement authorization.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresEngagementPermissionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiresEngagementPermissionAttribute" /> class.
    /// </summary>
    /// <param name="actionType">The action type checked against engagement rules.</param>
    public RequiresEngagementPermissionAttribute(string actionType)
    {
        ActionType = actionType;
    }

    /// <summary>
    /// Gets the action type checked against engagement rules.
    /// </summary>
    public string ActionType { get; }
}
