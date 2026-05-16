using Microsoft.Extensions.Logging;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker;

/// <summary>
/// Builds the final prompt from gated context components.
/// Assembles system prompt, wiki facts, conversation history,
/// and RAG LeanKernels into message lists for the LLM.
/// </summary>
public sealed class PromptAssembler
{
    private readonly ILogger<PromptAssembler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptAssembler" /> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public PromptAssembler(ILogger<PromptAssembler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Assemble context into a single string (for logging/debugging).
    /// </summary>
    public string Assemble(ConversationContext context)
    {
        var parts = new List<string> { context.SystemPrompt };
        AppendSourceSections(parts, context.WikiLeanKernels, context.RetrievedLeanKernels, compactHeaders: true);

        if (context.History.Count > 0)
        {
            parts.Add("\n--- Conversation ---");
            foreach (var turn in context.History)
            {
                var prefix = turn.Role == "user" ? "User" : "LeanKernel";
                var marker = turn.IsCompacted ? " [compacted]" : "";
                parts.Add($"{prefix}{marker}: {turn.Content}");
            }
        }

        var assembled = string.Join("\n", parts);
        _logger.LogDebug("Prompt assembled: {Length} chars", assembled.Length);
        return assembled;
    }

    /// <summary>
    /// Build the system message with injected knowledge LeanKernels.
    /// This is what gets sent as the system message to the LLM.
    /// </summary>
    public string AssembleSystemMessage(ConversationContext context)
    {
        var parts = new List<string> { context.SystemPrompt };
        AppendSourceSections(parts, context.WikiLeanKernels, context.RetrievedLeanKernels, compactHeaders: false);

        if (context.ActiveToolNames.Count > 0)
        {
            parts.Add($"\n## Available Tools: {string.Join(", ", context.ActiveToolNames)}");
        }

        if (!string.IsNullOrWhiteSpace(context.OnboardingInstruction))
        {
            parts.Add($"\n## Onboarding Directive\n{context.OnboardingInstruction}");
        }

        if (context.DisambiguationHints.Count > 0)
        {
            parts.Add("\n## Disambiguation");
            foreach (var hint in context.DisambiguationHints)
            {
                parts.Add($"- {hint}");
            }
        }

        return string.Join("\n", parts);
    }

    private static void AppendSourceSections(
        List<string> parts,
        IReadOnlyList<RelevanceScore> wikiCandidates,
        IReadOnlyList<RelevanceScore> retrievedCandidates,
        bool compactHeaders)
    {
        var wiki = wikiCandidates
            .Concat(retrievedCandidates.Where(x => x.KnowledgeSource == KnowledgeSourceType.Wiki))
            .OrderByDescending(entry => GetPriorityRank(entry.Priority))
            .ThenByDescending(entry => entry.Score)
            .ToList();
        var documents = retrievedCandidates
            .Where(x => x.KnowledgeSource != KnowledgeSourceType.Wiki)
            .ToList();

        if (wiki.Count > 0)
        {
            parts.Add(compactHeaders ? "\n--- Wiki ---" : "\n## Wiki");
            foreach (var entry in wiki)
            {
                parts.Add(compactHeaders
                    ? SanitizeRenderedText(entry.Content)
                    : $"- {SanitizeRenderedText(entry.Content)}");
            }
        }

        if (documents.Count > 0)
        {
            parts.Add(compactHeaders ? "\n--- Documents ---" : "\n## Documents");
            foreach (var entry in documents)
            {
                parts.Add(compactHeaders
                    ? SanitizeRenderedText(entry.Content)
                    : $"- {SanitizeRenderedText(entry.Content)}");
            }
        }
    }

    private static string SanitizeRenderedText(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return input
            .Replace("/app/data/wiki", "wiki", StringComparison.OrdinalIgnoreCase)
            .Replace("/app/data/documents", "documents", StringComparison.OrdinalIgnoreCase)
            .Replace("data/wiki/", "wiki/", StringComparison.OrdinalIgnoreCase)
            .Replace("data/documents/", "documents/", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetPriorityRank(ContextPriority priority) => priority switch
    {
        ContextPriority.Critical => 4,
        ContextPriority.High => 3,
        ContextPriority.Medium => 2,
        ContextPriority.Low => 1,
        _ => 0
    };
}
