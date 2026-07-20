using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Builds user-facing onboarding directives from detected profile gaps.
/// </summary>
public sealed class OnboardingDirectiveBuilder : IOnboardingDirectiveBuilder
{
    /// <inheritdoc />
    public string BuildDirective(CompletedTurnEvent turnEvent, IReadOnlyList<string> gaps)
    {
        if (gaps.Count == 0)
        {
            return "No onboarding gaps were detected.";
        }

        var ask = string.Join(", ", gaps.Select(static gap => gap switch
        {
            "name" => "their preferred name",
            "email" => "their email address",
            "timezone" => "their timezone",
            "language" => "their preferred language",
            _ => gap
        }));

        return $"On a suitable future turn, politely ask the user for {ask}. Session={turnEvent.SessionId ?? "unknown"}; Turn={turnEvent.TurnId}.";
    }
}
