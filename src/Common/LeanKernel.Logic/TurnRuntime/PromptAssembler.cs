using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Assembles the final prompt from admitted context items and shaped history.
/// Renders admitted items in stable source order and prepends them as system/user
/// context messages before the conversation history.
/// </summary>
public sealed class PromptAssembler(
    IOptions<AgentSettings> agentSettings,
    ILogger<PromptAssembler> logger) : ITurnStage
{
    /// <inheritdoc />
    public string Name => "PromptAssembler";

    /// <inheritdoc />
    public Task ExecuteAsync(TurnContext context, CancellationToken cancellationToken = default)
    {
        context.Prompt.Clear();

        // 1. System message with agent instructions
        var instructions = agentSettings.Value.DefaultInstructions;
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            context.Prompt.Add(new ChatMessage(ChatRole.System, instructions));
        }

        // 2. Admitted context items in stable source order
        var orderedAdmitted = context.Admitted
            .OrderBy(c => c.Source switch
            {
                "system" => 0,
                "identity" => 1,
                "memory" => 2,
                "retrieval" => 3,
                _ => 4
            })
            .ThenBy(c => c.Content, StringComparer.Ordinal)
            .ToList();

        if (orderedAdmitted.Count > 0)
        {
            var contextBlock = string.Join(
                "\n\n",
                orderedAdmitted.Select(c => $"[{c.Source}]\n{c.Content}"));

            context.Prompt.Add(new ChatMessage(
                ChatRole.User,
                $"Context for this conversation:\n```\n{contextBlock}\n```"));
        }

        // 3. Shaped history
        foreach (var historyMessage in context.ShapedHistory)
        {
            context.Prompt.Add(historyMessage);
        }

        // 4. Current user message (always last)
        context.Prompt.Add(new ChatMessage(ChatRole.User, context.UserMessage));

        logger.LogDebug(
            "Prompt assembled: {MessageCount} messages ({AdmittedCount} context items, {HistoryCount} history turns).",
            context.Prompt.Count, orderedAdmitted.Count, context.ShapedHistory.Count);

        return Task.CompletedTask;
    }
}