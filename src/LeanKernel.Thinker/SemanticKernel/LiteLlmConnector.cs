using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.SemanticKernel;

/// <summary>
/// Connects the gated ConversationContext to Semantic Kernel's chat API.
/// Translates LeanKernel's internal context format into SK ChatHistory.
/// </summary>
public static class LiteLlmConnector
{
    /// <summary>
    /// Build a ChatHistory from the gated context, then invoke the LLM.
    /// Returns the assistant's response text.
    /// </summary>
    public static async Task<string> InvokeAsync(
        Kernel kernel,
        ConversationContext context,
        string userQuery,
        CancellationToken ct)
    {
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history = BuildChatHistory(context, userQuery);

        var response = await chatService.GetChatMessageContentAsync(
            history,
            cancellationToken: ct);

        return response.Content ?? string.Empty;
    }

    /// <summary>
    /// Convert the gated ConversationContext into SK ChatHistory format.
    /// </summary>
    public static ChatHistory BuildChatHistory(ConversationContext context, string userQuery)
    {
        var history = new ChatHistory();

        // System prompt with injected knowledge
        var systemParts = new List<string> { context.SystemPrompt };

        if (context.WikiLeanKernels.Count > 0)
        {
            systemParts.Add("\n## Relevant Knowledge");
            foreach (var LeanKernel in context.WikiLeanKernels)
                systemParts.Add($"- {LeanKernel.Content}");
        }

        if (context.RetrievedLeanKernels.Count > 0)
        {
            systemParts.Add("\n## Related Context");
            foreach (var LeanKernel in context.RetrievedLeanKernels)
                systemParts.Add($"- {LeanKernel.Content}");
        }

        if (context.ActiveToolNames.Count > 0)
        {
            systemParts.Add($"\n## Available Tools: {string.Join(", ", context.ActiveToolNames)}");
        }

        history.AddSystemMessage(string.Join("\n", systemParts));

        // Conversation history (already compacted by gatekeeper)
        foreach (var turn in context.History)
        {
            if (turn.Role == "user")
                history.AddUserMessage(turn.Content);
            else
                history.AddAssistantMessage(turn.Content);
        }

        // Current user query
        history.AddUserMessage(userQuery);

        return history;
    }
}
