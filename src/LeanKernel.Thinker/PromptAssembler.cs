using Microsoft.Extensions.Logging;
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

        if (context.WikiLeanKernels.Count > 0)
        {
            parts.Add("\n--- Relevant Knowledge ---");
            foreach (var LeanKernel in context.WikiLeanKernels)
                parts.Add(LeanKernel.Content);
        }

        if (context.RetrievedLeanKernels.Count > 0)
        {
            parts.Add("\n--- Related Context ---");
            foreach (var LeanKernel in context.RetrievedLeanKernels)
                parts.Add(LeanKernel.Content);
        }

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

        if (context.WikiLeanKernels.Count > 0)
        {
            parts.Add("\n## Relevant Knowledge");
            foreach (var LeanKernel in context.WikiLeanKernels)
                parts.Add($"- {LeanKernel.Content}");
        }

        if (context.RetrievedLeanKernels.Count > 0)
        {
            parts.Add("\n## Related Context");
            foreach (var LeanKernel in context.RetrievedLeanKernels)
                parts.Add($"- {LeanKernel.Content}");
        }

        if (context.ActiveToolNames.Count > 0)
        {
            parts.Add($"\n## Available Tools: {string.Join(", ", context.ActiveToolNames)}");
        }

        return string.Join("\n", parts);
    }
}
