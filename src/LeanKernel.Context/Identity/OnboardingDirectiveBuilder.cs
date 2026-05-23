using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.Identity;

/// <summary>
/// Builds concise additive onboarding guidance from detected identity gaps.
/// </summary>
public sealed class OnboardingDirectiveBuilder
{
    private readonly IdentityConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnboardingDirectiveBuilder"/> class.
    /// </summary>
    /// <param name="config">The identity configuration.</param>
    public OnboardingDirectiveBuilder(IOptions<IdentityConfig> config)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Builds onboarding guidance from the detected gaps.
    /// </summary>
    /// <param name="result">The onboarding result to convert into guidance.</param>
    /// <returns>A concise onboarding directive, or <see langword="null"/> when no guidance is needed.</returns>
    public string? BuildDirective(OnboardingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.HasGaps || result.Gaps.Count == 0)
        {
            return null;
        }

        var selectedGaps = result.Gaps
            .GroupBy(static gap => gap.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(static gap => GetPriority(gap.GapCode)).ThenBy(static gap => gap.GapCode, StringComparer.Ordinal).First())
            .OrderBy(static gap => GetPriority(gap.GapCode))
            .ThenBy(static gap => gap.FieldName, StringComparer.Ordinal)
            .Take(Math.Max(1, _config.MaxOnboardingQuestionsPerTurn))
            .ToList();

        if (selectedGaps.Count == 0)
        {
            return null;
        }

        var lines = new List<string>
        {
            "Continue answering the user's current request.",
            "If it fits naturally, ask these brief onboarding follow-up questions:"
        };

        foreach (var gap in selectedGaps)
        {
            lines.Add($"- {BuildQuestion(gap.FieldName)}");
        }

        return string.Join("\n", lines);
    }

    private static int GetPriority(string gapCode)
        => gapCode.StartsWith("missing_", StringComparison.OrdinalIgnoreCase) || gapCode.StartsWith("placeholder_", StringComparison.OrdinalIgnoreCase)
            ? 0
            : gapCode.StartsWith("weak_", StringComparison.OrdinalIgnoreCase)
                ? 1
                : gapCode.StartsWith("stale_", StringComparison.OrdinalIgnoreCase)
                    ? 2
                    : 3;

    private static string BuildQuestion(string fieldName)
        => fieldName switch
        {
            "preferred_name" => "What name should I use for you?",
            "timezone" => "What timezone should I assume for schedules and time references?",
            "locale" => "What locale or regional format do you prefer?",
            "communication_style" => "How would you like me to communicate with you?",
            "work_style" => "What work style helps you most?",
            "recurring_goals" => "What recurring goals or priorities should I keep in mind?",
            "tool_preferences" => "Which tools or workflows do you prefer me to use?",
            "autonomy_level" => "How much autonomy would you like me to use before asking permission?",
            _ => $"What should I know about your {fieldName.Replace('_', ' ')}?",
        };
}
