using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Detects missing onboarding profile fields in user text.
/// </summary>
public sealed class OnboardingGapDetector : IOnboardingGapDetector
{
    /// <inheritdoc />
    public IReadOnlyList<string> DetectGaps(CompletedTurnEvent turnEvent)
    {
        var userText = string.Join(
            "\n",
            turnEvent.RequestMessages
                .Where(static message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                .Select(static message => message.Text));

        var normalized = userText.ToLowerInvariant();
        var gaps = new List<string>();

        if (!ContainsAny(normalized, ["my name is", "you can call me", "i am ", "i'm "]))
        {
            gaps.Add("name");
        }

        if (!ContainsAny(normalized, ["@", "my email is", "email me", "email address"]))
        {
            gaps.Add("email");
        }

        if (!ContainsAny(normalized, ["timezone", "time zone", "utc", "gmt", "pst", "est", "cst", "cet"]))
        {
            gaps.Add("timezone");
        }

        if (!ContainsAny(normalized, ["language", "i speak", "locale", "english", "spanish", "french", "german"]))
        {
            gaps.Add("language");
        }

        return gaps;
    }

    private static bool ContainsAny(string content, IReadOnlyCollection<string> candidates)
    {
        return candidates.Any(content.Contains);
    }
}
