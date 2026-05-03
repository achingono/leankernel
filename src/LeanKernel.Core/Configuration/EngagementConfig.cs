namespace LeanKernel.Core.Configuration;

/// <summary>
/// Represents the rules of engagement between user and agent(s).
/// Loaded from data/wiki/.LeanKernel/AGENTS.md at startup.
/// </summary>
public sealed class EngagementRules
{
    public AgentPersonality Personality { get; set; } = new();
    public AutonomyScope Autonomy { get; set; } = new();
    public TimeBoundaries TimeBoundaries { get; set; } = new();
    public ChannelRules ChannelRules { get; set; } = new();
    public ActionFollowUpRules ActionFollowUp { get; set; } = new();
    public MemoryPolicy MemoryPolicy { get; set; } = new();
    public SafetyBoundaries SafetyBoundaries { get; set; } = new();
    
    /// <summary>
    /// Version of the rules (for migration tracking).
    /// </summary>
    public string Version { get; set; } = "1";
    
    /// <summary>
    /// Last modified timestamp.
    /// </summary>
    public DateTimeOffset? LastModified { get; set; }
}

/// <summary>
/// How the agent should think and communicate.
/// </summary>
public sealed class AgentPersonality
{
    /// <summary>
    /// Tone preference: direct, conversational, etc.
    /// </summary>
    public string Tone { get; set; } = "direct, concise, authentic";
    
    /// <summary>
    /// Whether agent can have and express opinions.
    /// </summary>
    public bool AllowOpinions { get; set; } = true;
    
    /// <summary>
    /// Whether agent should try to solve problems before asking.
    /// </summary>
    public bool BeResourceful { get; set; } = true;
    
    /// <summary>
    /// Whether agent should admit uncertainty.
    /// </summary>
    public bool AdmitUncertainty { get; set; } = true;
}

/// <summary>
/// What the agent can do alone vs. must ask vs. never do.
/// </summary>
public sealed class AutonomyScope
{
    /// <summary>
    /// Actions the agent can perform without asking permission.
    /// Examples: "ReadFile", "AnalyzeCode", "UpdateWiki", "CreateNote"
    /// </summary>
    public string[] CanDoWithoutAsking { get; set; } = 
    [
        "ReadFile",
        "AnalyzeCode", 
        "SearchWiki",
        "CreateNote",
        "UpdateWiki"
    ];
    
    /// <summary>
    /// Actions that require explicit permission before executing.
    /// Examples: "SendEmail", "PushCode", "ModifyConfig", "DeleteFile"
    /// </summary>
    public string[] MustAskBefore { get; set; } = 
    [
        "SendEmail",
        "SendMessage",
        "PushCode",
        "ModifyConfig",
        "DeleteFile"
    ];
    
    /// <summary>
    /// Actions that are never allowed under any circumstances.
    /// Examples: "PushToProduction", "ExposeSecret", "DeleteWithoutConfirm"
    /// </summary>
    public string[] NeverDo { get; set; } = 
    [
        "PushToProduction",
        "ExposeSecret",
        "DeleteWithoutConfirm"
    ];
    
    /// <summary>
    /// Check if an action is authorized.
    /// </summary>
    public bool IsAuthorized(string action)
    {
        if (NeverDo.Contains(action, StringComparer.OrdinalIgnoreCase))
            return false;
        
        if (CanDoWithoutAsking.Contains(action, StringComparer.OrdinalIgnoreCase))
            return true;
        
        // If in MustAskBefore, return false (user will be asked)
        if (MustAskBefore.Contains(action, StringComparer.OrdinalIgnoreCase))
            return false;
        
        // Default: deny unknown actions
        return false;
    }
}

/// <summary>
/// Time boundaries for agent activity.
/// </summary>
public sealed class TimeBoundaries
{
    /// <summary>
    /// User's timezone (e.g., "Eastern", "Pacific")
    /// </summary>
    public string? Timezone { get; set; } = "Eastern";
    
    /// <summary>
    /// Active hours start (hour 0-23). Null = no boundary.
    /// </summary>
    public int? ActiveHoursStart { get; set; } = 8;
    
    /// <summary>
    /// Active hours end (hour 0-23). Null = no boundary.
    /// </summary>
    public int? ActiveHoursEnd { get; set; } = 22;
    
    /// <summary>
    /// Day of week that is Sabbath/rest day (0=Sunday, 6=Saturday).
    /// Null = no Sabbath.
    /// </summary>
    public DayOfWeek? SabbathDay { get; set; } = DayOfWeek.Saturday;
    
    /// <summary>
    /// Whether to allow messages during Sabbath.
    /// </summary>
    public bool AllowSabbathMessages { get; set; } = false;
    
    /// <summary>
    /// Whether to allow unsolicited messages during quiet hours.
    /// </summary>
    public bool AllowQuietHourMessages { get; set; } = false;
    
    /// <summary>
    /// Check if current time is within active hours.
    /// </summary>
    public bool IsWithinActiveHours(DateTime now, DayOfWeek dayOfWeek)
    {
        // Check Sabbath
        if (SabbathDay.HasValue && dayOfWeek == SabbathDay.Value && !AllowSabbathMessages)
            return false;
        
        // Check active hours
        if (ActiveHoursStart.HasValue && now.Hour < ActiveHoursStart.Value)
            return false;
        
        if (ActiveHoursEnd.HasValue && now.Hour >= ActiveHoursEnd.Value)
            return false;
        
        return true;
    }
    
    /// <summary>
    /// Get the next active window start time.
    /// </summary>
    public DateTime GetNextActiveWindow(DateTime from)
    {
        var hour = ActiveHoursStart ?? 8;
        var next = from.Date.AddHours(hour);
        
        if (next <= from)
            next = next.AddDays(1);
        
        return next;
    }
}

/// <summary>
/// Channel-specific communication rules.
/// </summary>
public sealed class ChannelRules
{
    /// <summary>
    /// Per-channel rules (e.g., "Signal", "Discord", "Email")
    /// </summary>
    public Dictionary<string, ChannelRuleSet> PerChannel { get; set; } = new();
    
    public ChannelRuleSet GetRulesFor(string channel)
    {
        return PerChannel.TryGetValue(channel, out var rules) ? rules : new();
    }
}

/// <summary>
/// Rules for a specific channel.
/// </summary>
public sealed class ChannelRuleSet
{
    /// <summary>
    /// Message format: "verbose", "brief", "default", etc.
    /// </summary>
    public string Format { get; set; } = "default";
    
    /// <summary>
    /// Whether to use emoji reactions instead of replying.
    /// </summary>
    public bool UseReactions { get; set; } = false;
    
    /// <summary>
    /// Maximum message fragments per response (to avoid spam).
    /// </summary>
    public int MaxMessageFragments { get; set; } = 1;
}

/// <summary>
/// Auto follow-up and reminder configuration.
/// </summary>
public sealed class ActionFollowUpRules
{
    /// <summary>
    /// Whether to automatically create follow-up tasks.
    /// </summary>
    public bool AutoTrackFollowUps { get; set; } = true;
    
    /// <summary>
    /// Default follow-up due dates by action type (in days).
    /// </summary>
    public Dictionary<string, int> DefaultFollowUpDays { get; set; } = new()
    {
        { "SendMessage", 7 },
        { "SubmitApplication", 14 },
        { "ScheduleEvent", 1 },
        { "DecisionAwaitingResponse", 3 }
    };
    
    public int GetFollowUpDays(string actionType)
    {
        return DefaultFollowUpDays.TryGetValue(actionType, out var days) ? days : 7;
    }
}

/// <summary>
/// What to capture and what's sensitive.
/// </summary>
public sealed class MemoryPolicy
{
    /// <summary>
    /// Categories of information to capture (e.g., "decisions", "lessons", "preferences")
    /// </summary>
    public string[] WhatToCapture { get; set; } = 
    [
        "decisions",
        "lessons_learned",
        "preferences",
        "patterns",
        "context"
    ];
    
    /// <summary>
    /// Sensitive data patterns that should not be captured (regex patterns).
    /// </summary>
    public string[] SensitivePatterns { get; set; } = 
    [
        @"api[_-]?key\s*[:=]",
        @"password\s*[:=]",
        @"secret\s*[:=]",
        @"token\s*[:=]",
        @"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b" // Credit card pattern
    ];
    
    /// <summary>
    /// Whether to update SOUL.md with learnings.
    /// </summary>
    public bool UpdateSoulMd { get; set; } = true;
    
    /// <summary>
    /// Whether to update USER.md with learnings.
    /// </summary>
    public bool UpdateUserMd { get; set; } = true;
}

/// <summary>
/// Hard boundaries on data and behavior.
/// </summary>
public sealed class SafetyBoundaries
{
    /// <summary>
    /// Whether to allow exporting data outside the system.
    /// </summary>
    public bool AllowExternalDataExport { get; set; } = false;
    
    /// <summary>
    /// Whether to require email drafts for review before sending.
    /// </summary>
    public bool RequireEmailDraft { get; set; } = true;
    
    /// <summary>
    /// Whether to require code review before pushing.
    /// </summary>
    public bool RequireCodeReview { get; set; } = true;
    
    /// <summary>
    /// Action types that are never allowed (additional to AutonomyScope.NeverDo).
    /// </summary>
    public string[] NeverExternalActionTypes { get; set; } = [];
}

/// <summary>
/// Service interface for loading engagement rules.
/// </summary>
public interface IEngagementRulesProvider
{
    /// <summary>
    /// Load engagement rules from AGENTS.md.
    /// </summary>
    Task<EngagementRules> LoadAsync(CancellationToken ct);
    
    /// <summary>
    /// Get current engagement rules (cached).
    /// </summary>
    EngagementRules GetCurrent();
}
