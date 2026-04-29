using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker;

/// <summary>
/// Builds the final prompt from gated context components.
/// Assembles system prompt, wiki facts, conversation history,
/// and RAG LeanKernels into a single prompt payload.
/// </summary>
public sealed class PromptAssembler
{
    private readonly ILogger<PromptAssembler> _logger;

    public PromptAssembler(ILogger<PromptAssembler> logger)
    {
        _logger = logger;
    }

    public string Assemble(ConversationContext context)
    {
        var parts = new List<string>
        {
            // System prompt
            context.SystemPrompt
        };

        // Wiki facts (compressed key-value format)
        if (context.WikiLeanKernels.Count > 0)
        {
            parts.Add("\n--- Relevant Knowledge ---");
            foreach (var LeanKernel in context.WikiLeanKernels)
            {
                parts.Add(LeanKernel.Content);
            }
        }

        // Retrieved LeanKernels
        if (context.RetrievedLeanKernels.Count > 0)
        {
            parts.Add("\n--- Related Context ---");
            foreach (var LeanKernel in context.RetrievedLeanKernels)
            {
                parts.Add(LeanKernel.Content);
            }
        }

        // Conversation history
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
}
