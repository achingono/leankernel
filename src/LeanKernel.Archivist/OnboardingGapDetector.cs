using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Archivist;

/// <summary>
/// Detects missing identity information and builds onboarding guidance for new sessions.
/// </summary>
public sealed class OnboardingGapDetector
{
    private readonly LeanKernelConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnboardingGapDetector" /> class.
    /// </summary>
    /// <param name="config">The LeanKernel configuration that identifies the active agent directory.</param>
    public OnboardingGapDetector(IOptions<LeanKernelConfig> config)
    {
        _config = config.Value;
    }

    /// <summary>
    /// Builds onboarding guidance when persisted identity files have missing or placeholder content.
    /// </summary>
    /// <param name="ct">A token used to cancel identity file reads.</param>
    /// <returns>An onboarding instruction, or <see langword="null" /> when no identity gaps are detected.</returns>
    public async Task<string?> BuildInstructionAsync(CancellationToken ct)
    {
        var agentDir = Path.Combine(_config.Agents.BasePath, "main");
        var soulPath = Path.Combine(agentDir, "SELF.md");
        var userPath = Path.Combine(agentDir, "USER.md");

        var soulContent = File.Exists(soulPath) ? await File.ReadAllTextAsync(soulPath, ct) : null;
        var userContent = File.Exists(userPath) ? await File.ReadAllTextAsync(userPath, ct) : null;

        var gaps = new List<string>();

        if (string.IsNullOrWhiteSpace(soulContent) || soulContent.Contains("TODO") || soulContent.Length < 100)
        {
            gaps.Add("agent identity (name, role, personality, capabilities, communication style)");
        }

        if (string.IsNullOrWhiteSpace(userContent) || userContent.Contains("TODO") || userContent.Length < 50)
        {
            gaps.Add("user preferences (name, timezone, communication preferences, goals)");
        }
        else
        {
            if (!userContent.Contains("name", StringComparison.OrdinalIgnoreCase) &&
                !userContent.Contains("who", StringComparison.OrdinalIgnoreCase))
            {
                gaps.Add("the user's name");
            }

            if (!userContent.Contains("timezone", StringComparison.OrdinalIgnoreCase) &&
                !userContent.Contains("location", StringComparison.OrdinalIgnoreCase))
            {
                gaps.Add("the user's timezone or location");
            }
        }

        if (gaps.Count == 0)
        {
            return null;
        }

        return $"""
            IMPORTANT: This is the first message in a new conversation session.
            Before answering the user's question, briefly introduce yourself and ask 1-2 focused questions
            to fill in missing information about: {string.Join(", ", gaps)}.
            Keep it conversational and natural — don't make it feel like a form.
            After gathering this context, answer their question.
            Once you have gathered the information, output it as a structured block at the end of your response:
            [IDENTITY_UPDATE]
            <the gathered facts as key: value pairs>
            [/IDENTITY_UPDATE]
            """;
    }
}
