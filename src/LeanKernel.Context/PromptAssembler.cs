using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Context;

/// <summary>
/// Builds the final instruction manifest from admitted context segments.
/// Each segment has a known source and is inspectable.
/// </summary>
public sealed class PromptAssembler
{
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<PromptAssembler> _logger;

    public PromptAssembler(ITokenEstimator tokenEstimator, ILogger<PromptAssembler> logger)
    {
        _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Assembles the system message from context components.
    /// </summary>
    public string AssembleSystemMessage(ConversationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var parts = new List<string>
        {
            context.SystemPrompt,
        };

        if (context.Identity?.PromptSegments.Count > 0)
        {
            parts.Add("\n## Identity Context");
            parts.AddRange(context.Identity.PromptSegments);
        }

        if (!string.IsNullOrWhiteSpace(context.Onboarding?.OnboardingDirective))
        {
            parts.Add("\n## Onboarding Guidance");
            parts.Add(context.Onboarding.OnboardingDirective!);
        }

        if (context.WikiFacts.Count > 0)
        {
            parts.Add("\n## Relevant Knowledge");
            foreach (var fact in context.WikiFacts)
            {
                parts.Add($"- [{fact.Source}] {fact.Content}");
            }
        }

        if (context.RetrievedKnowledge.Count > 0)
        {
            parts.Add("\n## Retrieved Context");
            foreach (var item in context.RetrievedKnowledge)
            {
                parts.Add($"- [{item.Source}:{item.Key}] {item.Content}");
            }
        }

        if (context.ActiveToolNames.Count > 0)
        {
            parts.Add($"\n## Available Tools: {string.Join(", ", context.ActiveToolNames)}");
            parts.Add("You have access to the functions listed above. When a user asks you to do something that requires a tool, use the function call mechanism rather than describing what you would do. If you're unsure which tool to use, think step by step and call the appropriate function with the correct parameters.");
        }

        var hasKnowledge = context.WikiFacts.Count > 0 || context.RetrievedKnowledge.Count > 0;
        if (hasKnowledge || context.ActiveToolNames.Any(n => n.StartsWith("wiki_", StringComparison.Ordinal)))
        {
            parts.Add("\n## Knowledge-First Policy");
            parts.Add("Always check your available knowledge before asking the user for information they may have already provided. First, review the knowledge sections above — the answer may already be there. If not, use wiki_search or wiki_read to look it up. Only ask the user for clarification if you cannot find the information anywhere in your knowledge base.");
        }

        var assembled = string.Join("\n", parts);
        var tokens = _tokenEstimator.EstimateTokens(assembled);

        _logger.LogDebug("System message assembled: {Tokens} tokens, {Parts} parts", tokens, parts.Count);

        return assembled;
    }

    /// <summary>
    /// Builds the full prompt representation for debugging/diagnostics.
    /// </summary>
    public string AssembleFullPrompt(ConversationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var parts = new List<string> { AssembleSystemMessage(context) };

        if (context.History.Count > 0)
        {
            parts.Add("\n--- Conversation ---");
            foreach (var turn in context.History)
            {
                var prefix = string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase)
                    ? "User"
                    : "Assistant";
                var marker = turn.IsCompacted ? " [compacted]" : string.Empty;
                parts.Add($"{prefix}{marker}: {turn.Content}");
            }
        }

        return string.Join("\n", parts);
    }
}
