using System.Text.RegularExpressions;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Embedding-based extractive history compactor. Segments turns into sentences,
/// embeds them, scores by cosine similarity to the most recent user message,
/// and selects the top-K most salient sentences in original order.
/// </summary>
public sealed partial class EmbeddingHistoryCompactor(
    IEmbeddingClient embeddingClient,
    IOptions<TurnPipelineSettings> settings,
    ILogger<EmbeddingHistoryCompactor> logger) : IHistoryCompactor
{
    private readonly TurnPipelineSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<string?> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return null;
        }

        var allSentences = SegmentAllSentences(messages);
        if (allSentences.Count <= 1)
        {
            return allSentences.Count == 1 ? allSentences[0].Text : null;
        }

        var referenceText = FindReferenceText(messages);
        if (string.IsNullOrWhiteSpace(referenceText))
        {
            logger.LogDebug("No reference text found for compaction scoring; skipping.");
            return null;
        }

        var inputTexts = allSentences.Select(s => s.Text).ToList();
        inputTexts.Add(referenceText);

        var embeddings = await embeddingClient
            .GetEmbeddingsAsync(inputTexts, _settings.CompactionEmbeddingModel, cancellationToken)
            .ConfigureAwait(false);

        if (embeddings.Count < inputTexts.Count)
        {
            logger.LogWarning(
                "Embedding call returned {Got} vectors for {Expected} inputs; skipping compaction.",
                embeddings.Count, inputTexts.Count);
            return null;
        }

        var referenceEmbedding = embeddings[^1];
        var sentenceEmbeddings = embeddings.Take(allSentences.Count).ToList();

        var scored = allSentences
            .Zip(sentenceEmbeddings, (sentence, embedding) =>
                new ScoredSentence(sentence, CosineSimilarity(referenceEmbedding, embedding)))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Sentence.OriginalOrder)
            .Take(_settings.CompactionMaxSentences)
            .OrderBy(s => s.Sentence.OriginalOrder)
            .ToList();

        var result = string.Join(" ", scored.Select(s => s.Sentence.Text));

        logger.LogDebug(
            "Compacted {Total} sentences to {Kept} (score range {MinScore:F3}–{MaxScore:F3}).",
            allSentences.Count, scored.Count,
            scored.Min(s => s.Score), scored.Max(s => s.Score));

        return result;
    }

    private static List<SentenceInfo> SegmentAllSentences(IReadOnlyList<ChatMessage> messages)
    {
        var sentences = new List<SentenceInfo>();
        var order = 0;

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            var parts = SentenceSplitter().Split(message.Text.Trim());
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    sentences.Add(new SentenceInfo(trimmed, order++));
                }
            }
        }

        return sentences;
    }

    private static string? FindReferenceText(IReadOnlyList<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.User && !string.IsNullOrWhiteSpace(messages[i].Text))
            {
                return messages[i].Text!.Trim();
            }
        }

        return null;
    }

    private static float CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }

        var spanA = a.Span;
        var spanB = b.Span;

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < spanA.Length; i++)
        {
            dot += spanA[i] * spanB[i];
            normA += spanA[i] * spanA[i];
            normB += spanB[i] * spanB[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator > 0 ? (float)(dot / denominator) : 0f;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z""'\d])")]
    private static partial Regex SentenceSplitter();

    private sealed record SentenceInfo(string Text, int OriginalOrder);

    private sealed record ScoredSentence(SentenceInfo Sentence, float Score);
}