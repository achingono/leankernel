namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configures durable identity loading, onboarding guidance, and identity writeback behavior.
/// </summary>
public sealed class IdentityConfig
{
    /// <summary>
    /// Gets or sets the GBrain page key for the main agent profile page.
    /// </summary>
    public string AgentProfilePageKey { get; set; } = "identity-agent-main";

    /// <summary>
    /// Gets or sets the GBrain page key for the default user-preference page.
    /// </summary>
    public string UserPreferencePageKey { get; set; } = "identity-user-default";

    /// <summary>
    /// Gets or sets a value indicating whether identity page keys are derived from the
    /// caller's userId (namespaced per user). When <c>false</c> (default), the fixed
    /// <see cref="AgentProfilePageKey"/> and <see cref="UserPreferencePageKey"/> are used.
    /// Enable for multi-tenant deployments where each user has isolated identity pages.
    /// </summary>
    public bool EnableUserScopedKeys { get; set; }

    /// <summary>
    /// Gets or sets the confidence threshold below which onboarding guidance is emitted.
    /// </summary>
    public double OnboardingConfidenceThreshold { get; set; } = 0.6;

    /// <summary>
    /// Gets or sets the maximum number of onboarding questions that may be asked in a turn.
    /// </summary>
    public int MaxOnboardingQuestionsPerTurn { get; set; } = 2;

    /// <summary>
    /// Gets or sets a value indicating whether post-turn identity extraction is enabled.
    /// </summary>
    public bool EnableIdentityExtraction { get; set; } = true;

    /// <summary>
    /// Gets or sets the allowlisted identity field names that may be updated.
    /// </summary>
    public List<string> AllowedIdentityFields { get; set; } =
    [
        "preferred_name",
        "timezone",
        "locale",
        "communication_style",
        "work_style",
        "recurring_goals",
        "tool_preferences",
        "autonomy_level"
    ];
}
