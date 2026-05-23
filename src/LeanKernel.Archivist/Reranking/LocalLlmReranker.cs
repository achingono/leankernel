using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Reranking;

/// <summary>
/// Lightweight local reranker that boosts lexical relevance over initial retrieval order.
/// </summary>
public sealed class LocalLlmReranker : IReranker
{
    private static readonly char[] Delimiters =
        [' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_'];

    /// <inheritdoc />
    public Task<IReadOnlyList<RelevanceScore>> RerankAsync(
        string query,
        IReadOnlyList<RelevanceScore> candidates,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query) || candidates.Count == 0)
        {
            return Task.FromResult(candidates);
        }

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            return Task.FromResult(candidates);
        }

        var reranked = candidates
            .Select(c =>
            {
                ct.ThrowIfCancellationRequested();
                var lexical = ComputeLexicalOverlap(queryTokens, c.Content);
                var baseScore = c.SourceType == RelevanceSourceType.Vector
                    ? c.SemanticSimilarity
                    : c.Score;
                var rerankScore = Math.Clamp((baseScore * 0.6) + (lexical * 0.4), 0.0, 1.0);
                return c with { Score = rerankScore };
            })
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.SemanticSimilarity)
            .ToList();

        return Task.FromResult<IReadOnlyList<RelevanceScore>>(reranked);
    }

    private static HashSet<string> Tokenize(string text)
        => text.ToLowerInvariant()
            .Split(Delimiters, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2)
            .ToHashSet(StringComparer.Ordinal);

    private static double ComputeLexicalOverlap(HashSet<string> queryTokens, string candidateText)
    {
        if (string.IsNullOrWhiteSpace(candidateText) || queryTokens.Count == 0)
        {
            return 0.0;
        }

        var candidateTokens = Tokenize(candidateText);
        if (candidateTokens.Count == 0)
        {
            return 0.0;
        }

        var overlap = queryTokens.Count(token => candidateTokens.Contains(token));
        return (double)overlap / queryTokens.Count;
    }
}

