using System.ComponentModel.DataAnnotations;
using System.Text;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Services;

/// <summary>
/// Provides functionality for onboarding service.
/// </summary>
public sealed class OnboardingService(
    IKnowledgeService knowledgeService,
    IOnboardingDetector onboardingDetector,
    ILogger<OnboardingService> logger)
{
    public const string UserProfilePageKey = "wiki/identity/user-profile";
    public const string UserGoalsPageKey = "wiki/identity/user-goals";

    private static readonly HashSet<string> SupportedGapFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "preferred_name",
        "communication_style",
        "timezone",
        "locale",
        "recurring_goals"
    };

    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly IOnboardingDetector _onboardingDetector = onboardingDetector ?? throw new ArgumentNullException(nameof(onboardingDetector));
    private readonly ILogger<OnboardingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Executes load async.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>The operation result.</returns>
    public async Task<OnboardingLoadResult> LoadAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var profileTask = _knowledgeService.GetPageAsync(UserProfilePageKey, ct);
        var goalsTask = _knowledgeService.GetPageAsync(UserGoalsPageKey, ct);
        await Task.WhenAll(profileTask, goalsTask).ConfigureAwait(false);

        var draft = new OnboardingDraft();
        ApplyProfilePage(draft, profileTask.Result);
        ApplyGoalsPage(draft, goalsTask.Result);
        NormalizeDraft(draft);

        return new OnboardingLoadResult
        {
            Draft = draft,
            HasExistingProfile = profileTask.Result is not null || goalsTask.Result is not null,
            Gaps = await DetectGapsAsync(userId, draft, ct).ConfigureAwait(false)
        };
    }

    public async Task<IReadOnlyList<OnboardingGapInsight>> DetectGapsAsync(
        string userId,
        OnboardingDraft draft,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(draft);

        var now = DateTimeOffset.UtcNow;
        var fields = new Dictionary<string, IdentityField>(StringComparer.OrdinalIgnoreCase);
        AddIdentityField(fields, "preferred_name", draft.DisplayName, now);
        AddIdentityField(fields, "communication_style", draft.CommunicationStyle, now);
        AddIdentityField(fields, "timezone", draft.Timezone, now);
        AddIdentityField(fields, "locale", draft.PreferredLanguage, now);
        AddIdentityField(fields, "recurring_goals", BuildRecurringGoalsValue(draft), now);

        var onboarding = await _onboardingDetector.DetectGapsAsync(new IdentityContext
        {
            UserId = userId,
            UserPreferences = new IdentityPage
            {
                Key = UserProfilePageKey,
                Content = string.Empty,
                Fields = fields
            }
        }, ct).ConfigureAwait(false);

        return onboarding.Gaps
            .Where(static gap => SupportedGapFields.Contains(gap.FieldName))
            .Select(MapGap)
            .ToList();
    }

    /// <summary>
    /// Executes save async.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="draft">The draft.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>The operation result.</returns>
    public async Task<OnboardingSaveResult> SaveAsync(string userId, OnboardingDraft draft, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(draft);

        NormalizeDraft(draft);

        var profileContent = BuildProfilePageContent(userId, draft);
        var goalsContent = BuildGoalsPageContent(userId, draft);
        var errors = new List<string>();

        try
        {
            await SavePageIfChangedAsync(UserProfilePageKey, profileContent, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            errors.Add("The user profile page could not be saved.");
            _logger.LogError(ex, "Saving onboarding profile page {PageKey} failed", UserProfilePageKey);
        }

        try
        {
            await SavePageIfChangedAsync(UserGoalsPageKey, goalsContent, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            errors.Add("The user goals page could not be saved.");
            _logger.LogError(ex, "Saving onboarding goals page {PageKey} failed", UserGoalsPageKey);
        }

        return new OnboardingSaveResult
        {
            IsSuccess = errors.Count == 0,
            Message = errors.Count switch
            {
                0 => "Profile saved to GBrain wiki pages.",
                1 => errors[0],
                _ => "The onboarding profile could not be saved completely."
            },
            Errors = errors,
            Gaps = await DetectGapsAsync(userId, draft, ct).ConfigureAwait(false)
        };
    }

    private async Task SavePageIfChangedAsync(string key, string content, CancellationToken ct)
    {
        var existingPage = await _knowledgeService.GetPageAsync(key, ct).ConfigureAwait(false);
        if (string.Equals(existingPage?.Content?.Trim(), content.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        await _knowledgeService.PutPageAsync(key, content, ct).ConfigureAwait(false);
    }

    private static void ApplyProfilePage(OnboardingDraft draft, KnowledgePage? page)
    {
        if (page is null)
        {
            return;
        }

        var document = ParseMarkdownDocument(page.Content);
        draft.DisplayName = GetFirstValue(document.Frontmatter, "display_name", "preferred_name", "name");
        draft.RoleTitle = GetFirstValue(document.Frontmatter, "role", "title");
        draft.CommunicationStyle = GetFirstValue(document.Frontmatter, "communication_style", "style", "tone");
        draft.Timezone = GetFirstValue(document.Frontmatter, "timezone", "time_zone");
        draft.PreferredLanguage = GetFirstValue(document.Frontmatter, "preferred_language", "locale", "language");
    }

    private static void ApplyGoalsPage(OnboardingDraft draft, KnowledgePage? page)
    {
        if (page is null)
        {
            return;
        }

        var document = ParseMarkdownDocument(page.Content);
        draft.Domains = ExtractBulletSection(document.Body, "Knowledge Domains");
        draft.Goals = ExtractBulletSection(document.Body, "Goals");
        draft.OtherGoals = ExtractTextSection(document.Body, "Other Goals");
    }

    private static void NormalizeDraft(OnboardingDraft draft)
    {
        draft.DisplayName = NormalizeSingleValue(draft.DisplayName);
        draft.RoleTitle = NormalizeSingleValue(draft.RoleTitle);
        draft.CommunicationStyle = NormalizeSingleValue(draft.CommunicationStyle);
        draft.Timezone = NormalizeSingleValue(draft.Timezone);
        draft.PreferredLanguage = NormalizeSingleValue(draft.PreferredLanguage);
        draft.OtherGoals = NormalizeParagraphValue(draft.OtherGoals);
        draft.Domains = NormalizeUniqueValues(draft.Domains);
        draft.Goals = NormalizeUniqueValues(draft.Goals);

        if (string.IsNullOrWhiteSpace(draft.CommunicationStyle))
        {
            draft.CommunicationStyle = "balanced";
        }
    }

    private static string BuildProfilePageContent(string userId, OnboardingDraft draft)
    {
        var frontmatter = new (string Key, string? Value)[]
        {
            ("page_type", "user-profile"),
            ("user_id", userId),
            ("name", draft.DisplayName),
            ("preferred_name", draft.DisplayName),
            ("role", draft.RoleTitle),
            ("style", draft.CommunicationStyle),
            ("communication_style", draft.CommunicationStyle),
            ("timezone", draft.Timezone),
            ("preferred_language", draft.PreferredLanguage),
            ("locale", draft.PreferredLanguage),
            ("updated_at", DateTimeOffset.UtcNow.ToString("O"))
        };

        var body = new StringBuilder()
            .AppendLine("# User Profile")
            .AppendLine()
            .AppendLine($"- Display name: {GetValueOrFallback(draft.DisplayName)}")
            .AppendLine($"- Role or title: {GetValueOrFallback(draft.RoleTitle)}")
            .AppendLine($"- Communication style: {GetValueOrFallback(draft.CommunicationStyle)}")
            .AppendLine($"- Timezone: {GetValueOrFallback(draft.Timezone)}")
            .AppendLine($"- Preferred language: {GetValueOrFallback(draft.PreferredLanguage)}")
            .ToString();

        return SerializeDocument(frontmatter, body);
    }

    private static string BuildGoalsPageContent(string userId, OnboardingDraft draft)
    {
        var frontmatter = new (string Key, string? Value)[]
        {
            ("page_type", "user-goals"),
            ("user_id", userId),
            ("recurring_goals", BuildRecurringGoalsValue(draft)),
            ("updated_at", DateTimeOffset.UtcNow.ToString("O"))
        };

        var body = new StringBuilder()
            .AppendLine("# User Goals")
            .AppendLine();

        AppendBulletSection(body, "Knowledge Domains", draft.Domains, "No domains added yet.");
        body.AppendLine();
        AppendBulletSection(body, "Goals", draft.Goals, "No goals selected yet.");
        body.AppendLine();
        body.AppendLine("## Other Goals");
        body.AppendLine(string.IsNullOrWhiteSpace(draft.OtherGoals) ? "Not provided yet." : draft.OtherGoals);

        return SerializeDocument(frontmatter, body.ToString());
    }

    private static void AppendBulletSection(StringBuilder builder, string heading, IReadOnlyList<string> items, string emptyState)
    {
        builder.AppendLine($"## {heading}");

        if (items.Count == 0)
        {
            builder.AppendLine($"- {emptyState}");
            return;
        }

        foreach (var item in items)
        {
            builder.AppendLine($"- {item}");
        }
    }

    private static string SerializeDocument(IEnumerable<(string Key, string? Value)> frontmatter, string body)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");

        foreach (var (key, value) in frontmatter)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            builder.AppendLine($"{key}: {QuoteYaml(value)}");
        }

        builder.AppendLine("---");
        builder.AppendLine(body.Trim());
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static ParsedMarkdownDocument ParseMarkdownDocument(string? content)
    {
        var normalized = NormalizeNewLines(content);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ParsedMarkdownDocument(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), string.Empty);
        }

        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return new ParsedMarkdownDocument(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized.Trim());
        }

        var closingIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return new ParsedMarkdownDocument(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized.Trim());
        }

        var frontmatter = normalized[4..closingIndex];
        var body = normalized[(closingIndex + 5)..].Trim();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = UnquoteYaml(line[(separatorIndex + 1)..].Trim());
            metadata[key] = value;
        }

        return new ParsedMarkdownDocument(metadata, body);
    }

    private static List<string> ExtractBulletSection(string body, string heading)
    {
        var sections = ParseBodySections(body);
        if (!sections.TryGetValue(heading, out var lines))
        {
            return [];
        }

        return lines
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(static line => line[2..].Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.EndsWith("yet.", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ExtractTextSection(string body, string heading)
    {
        var sections = ParseBodySections(body);
        if (!sections.TryGetValue(heading, out var lines))
        {
            return string.Empty;
        }

        var value = string.Join("\n", lines).Trim();
        return string.Equals(value, "Not provided yet.", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value;
    }

    private static Dictionary<string, List<string>> ParseBodySections(string body)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentSection = null;

        foreach (var rawLine in NormalizeNewLines(body).Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                currentSection = line[3..].Trim();
                sections[currentSection] = [];
                continue;
            }

            if (currentSection is null)
            {
                continue;
            }

            sections[currentSection].Add(line);
        }

        return sections;
    }

    private static void AddIdentityField(
        IDictionary<string, IdentityField> fields,
        string key,
        string? value,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        fields[key] = new IdentityField
        {
            Name = key,
            Value = value.Trim(),
            Confidence = 1.0,
            LastUpdated = updatedAt,
            Source = "blazor-onboarding"
        };
    }

    private static string BuildRecurringGoalsValue(OnboardingDraft draft)
    {
        var items = new List<string>();
        items.AddRange(draft.Goals);

        if (!string.IsNullOrWhiteSpace(draft.OtherGoals))
        {
            items.Add(draft.OtherGoals);
        }

        return string.Join("; ", NormalizeUniqueValues(items));
    }

    private static OnboardingGapInsight MapGap(IdentityGap gap)
        => gap.FieldName.ToLowerInvariant() switch
        {
            "preferred_name" => new OnboardingGapInsight(
                gap.FieldName,
                "Add your display name",
                "LeanKernel uses it to address you naturally and personalize responses."),
            "communication_style" => new OnboardingGapInsight(
                gap.FieldName,
                "Choose a communication style",
                "This keeps replies closer to your preferred tone without repeated prompting."),
            "timezone" => new OnboardingGapInsight(
                gap.FieldName,
                "Add your timezone",
                "Timezone helps LeanKernel phrase deadlines, scheduling, and time-based suggestions correctly."),
            "locale" => new OnboardingGapInsight(
                gap.FieldName,
                "Set your preferred language",
                "Language preferences help LeanKernel keep wording, spelling, and examples consistent."),
            "recurring_goals" => new OnboardingGapInsight(
                gap.FieldName,
                "Capture at least one goal",
                "Goals help LeanKernel prioritize the kinds of work and follow-up support you care about most."),
            _ => new OnboardingGapInsight(
                gap.FieldName,
                gap.FieldName,
                gap.Reason ?? "Additional profile context would improve future responses.")
        };

    private static string GetFirstValue(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static List<string> NormalizeUniqueValues(IEnumerable<string> values)
        => values
            .Select(NormalizeSingleValue)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeSingleValue(string? value)
        => string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizeParagraphValue(string? value)
        => NormalizeNewLines(value).Trim();

    private static string NormalizeNewLines(string? value)
        => (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string QuoteYaml(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string UnquoteYaml(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string GetValueOrFallback(string value)
        => string.IsNullOrWhiteSpace(value) ? "Not provided yet" : value;

    private sealed record ParsedMarkdownDocument(
        IReadOnlyDictionary<string, string> Frontmatter,
        string Body);
}

/// <summary>
/// Provides functionality for onboarding draft.
/// </summary>
public sealed class OnboardingDraft
{
    [Required(ErrorMessage = "Display name is required.")]
    [MaxLength(120, ErrorMessage = "Display name must be 120 characters or fewer.")]
    /// <summary>
    /// Gets or sets display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(120, ErrorMessage = "Role or title must be 120 characters or fewer.")]
    /// <summary>
    /// Gets or sets role title.
    /// </summary>
    public string RoleTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets communication style.
    /// </summary>
    public string CommunicationStyle { get; set; } = "balanced";

    [MaxLength(120, ErrorMessage = "Timezone must be 120 characters or fewer.")]
    /// <summary>
    /// Gets or sets timezone.
    /// </summary>
    public string Timezone { get; set; } = string.Empty;

    [MaxLength(120, ErrorMessage = "Preferred language must be 120 characters or fewer.")]
    /// <summary>
    /// Gets or sets preferred language.
    /// </summary>
    public string PreferredLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets domains.
    /// </summary>
    public List<string> Domains { get; set; } = [];

    /// <summary>
    /// Gets or sets goals.
    /// </summary>
    public List<string> Goals { get; set; } = [];

    [MaxLength(1200, ErrorMessage = "Other goals must be 1200 characters or fewer.")]
    /// <summary>
    /// Gets or sets other goals.
    /// </summary>
    public string OtherGoals { get; set; } = string.Empty;
}

/// <summary>
/// Provides functionality for onboarding load result.
/// </summary>
public sealed record OnboardingLoadResult
{
    /// <summary>
    /// Gets or sets draft.
    /// </summary>
    public required OnboardingDraft Draft { get; init; }

    /// <summary>
    /// Gets or sets has existing profile.
    /// </summary>
    public bool HasExistingProfile { get; init; }

    /// <summary>
    /// Gets or sets gaps.
    /// </summary>
    public IReadOnlyList<OnboardingGapInsight> Gaps { get; init; } = [];
}

/// <summary>
/// Provides functionality for onboarding save result.
/// </summary>
public sealed record OnboardingSaveResult
{
    /// <summary>
    /// Gets or sets is success.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets or sets message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets or sets gaps.
    /// </summary>
    public IReadOnlyList<OnboardingGapInsight> Gaps { get; init; } = [];
}

/// <summary>
/// Provides functionality for onboarding gap insight.
/// </summary>
public sealed record OnboardingGapInsight(string FieldName, string Title, string Detail);
