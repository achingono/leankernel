using LeanKernel.Logic.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Produces LLM summaries for older history turns using the small-model client.
/// </summary>
public sealed class HistorySummarizer(
    [FromKeyedServices("small-model")] IChatClient chatClient,
    IOptions<TurnPipelineSettings> settings,
    ILogger<HistorySummarizer> logger) : IHistorySummarizer
{
    private const int MaxTranscriptChars = 12000;
    private readonly TurnPipelineSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<string?> SummarizeAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return null;
        }

        var transcript = BuildTranscript(messages);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        try
        {
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(
                        ChatRole.System,
                        "Summarize the older conversation turns into concise, factual bullet points. Preserve commitments, decisions, constraints, and unresolved tasks. Return plain text only."),
                    new ChatMessage(ChatRole.User, transcript)
                ],
                new ChatOptions
                {
                    Temperature = (float)_settings.SummarizationTemperature,
                    MaxOutputTokens = _settings.SummarizationMaxOutputTokens
                },
                cancellationToken).ConfigureAwait(false);

            var text = response.Messages?.FirstOrDefault()?.Text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "History summarization failed. Falling back to verbatim summarized turns.");
            return null;
        }
    }

    private static string BuildTranscript(IReadOnlyList<ChatMessage> messages)
    {
        var lines = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message.Text))
            .Select(static message => $"{message.Role}: {message.Text!.Trim()}")
            .ToList();

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var transcript = string.Join('\n', lines);
        return transcript.Length <= MaxTranscriptChars
            ? transcript
            : transcript[..MaxTranscriptChars];
    }
}
