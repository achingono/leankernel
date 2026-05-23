using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.Identity;

/// <summary>
/// Detects missing, weak, placeholder, and stale identity fields that should trigger onboarding.
/// </summary>
public sealed class OnboardingGapDetector : IOnboardingDetector
{
    private static readonly HashSet<string> PlaceholderValues =
    [
        "todo",
        "tbd",
        "unknown",
        "n/a",
        "na",
        "unset",
        "none",
        "?"
    ];

    private readonly IdentityConfig _config;
    private readonly ILogger<OnboardingGapDetector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnboardingGapDetector"/> class.
    /// </summary>
    /// <param name="config">The identity configuration.</param>
    /// <param name="logger">The logger.</param>
    public OnboardingGapDetector(IOptions<IdentityConfig> config, ILogger<OnboardingGapDetector> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<OnboardingResult> DetectGapsAsync(IdentityContext identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        ct.ThrowIfCancellationRequested();

        var gaps = new List<IdentityGap>();
        var fields = identity.UserPreferences?.Fields ?? new Dictionary<string, IdentityField>(StringComparer.OrdinalIgnoreCase);

        foreach (var allowedField in _config.AllowedIdentityFields.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!fields.TryGetValue(allowedField, out var field) || string.IsNullOrWhiteSpace(field.Value))
            {
                gaps.Add(new IdentityGap
                {
                    FieldName = allowedField,
                    GapCode = $"missing_{allowedField}",
                    Reason = "Field is missing.",
                });
                continue;
            }

            if (IsPlaceholder(field.Value))
            {
                gaps.Add(new IdentityGap
                {
                    FieldName = allowedField,
                    GapCode = $"placeholder_{allowedField}",
                    Reason = "Field uses a placeholder value.",
                });
            }

            if (field.Confidence < _config.OnboardingConfidenceThreshold)
            {
                gaps.Add(new IdentityGap
                {
                    FieldName = allowedField,
                    GapCode = $"weak_{allowedField}",
                    Reason = $"Field confidence {field.Confidence:0.###} is below the onboarding threshold.",
                });
            }

            if (IsStale(allowedField, field.LastUpdated))
            {
                gaps.Add(new IdentityGap
                {
                    FieldName = allowedField,
                    GapCode = $"stale_{allowedField}",
                    Reason = "Field appears stale and should be refreshed.",
                });
            }
        }

        _logger.LogDebug("Detected {GapCount} onboarding gaps for user {UserId}", gaps.Count, identity.UserId);

        return Task.FromResult(new OnboardingResult
        {
            HasGaps = gaps.Count > 0,
            Gaps = gaps,
        });
    }

    private static bool IsPlaceholder(string value)
        => PlaceholderValues.Contains(value.Trim().ToLowerInvariant());

    private static bool IsStale(string fieldName, DateTimeOffset lastUpdated)
    {
        var age = DateTimeOffset.UtcNow - lastUpdated;
        return fieldName switch
        {
            "recurring_goals" => age > TimeSpan.FromDays(90),
            "tool_preferences" => age > TimeSpan.FromDays(180),
            "autonomy_level" => age > TimeSpan.FromDays(180),
            _ => age > TimeSpan.FromDays(365),
        };
    }
}
