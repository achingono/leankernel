using System.Text;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Enhances agent responses by synthesizing relevant knowledge from the document
/// and wiki base, making connections between conversations and documents.
/// 
/// Example: User discusses workplace conflict → service finds HBR articles
/// on conflict resolution and weaves them into the response.
/// </summary>
public sealed class KnowledgeEnhancementService : IResponseEnhancer
{
    private readonly IKnowledgeSearchService _knowledge;
    private readonly ILogger<KnowledgeEnhancementService> _logger;

    public KnowledgeEnhancementService(
        IKnowledgeSearchService knowledge,
        ILogger<KnowledgeEnhancementService> logger)
    {
        _knowledge = knowledge;
        _logger = logger;
    }

    /// <summary>
    /// Optionally enhance a response by searching for relevant knowledge.
    /// Returns either the enhanced response or the original response if enhancement fails.
    /// </summary>
    public async Task<string> EnhanceResponseAsync(
        string userQuery,
        string assistantResponse,
        ConversationContext context,
        CancellationToken ct)
    {
        try
        {
            // Skip enhancement if response is very short or looks like an error message
            if (assistantResponse.Length < 50 || 
                assistantResponse.Contains("encountered an error", StringComparison.OrdinalIgnoreCase))
            {
                return assistantResponse;
            }

            // Search for relevant knowledge
            // Use "general" tag to search across all available knowledge
            var searchTags = new List<string> { "general" };

            var searchResults = await _knowledge.SearchAsync(
                userQuery, 
                searchTags, 
                limit: 3, 
                ct: ct);

            if (searchResults.Count == 0)
            {
                return assistantResponse; // No relevant knowledge found
            }

            // Extract relevant insights from search results
            var relevantInsights = FormatInsights(searchResults);
            if (string.IsNullOrWhiteSpace(relevantInsights))
            {
                return assistantResponse;
            }

            // Append knowledge references to response
            var enhancedResponse = AppendKnowledgeReferences(assistantResponse, relevantInsights);
            
            _logger.LogDebug(
                "Enhanced response with {Count} knowledge references", 
                searchResults.Count);

            return enhancedResponse;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge enhancement failed — returning original response");
            return assistantResponse;
        }
    }

    /// <summary>
    /// Format search results into human-readable insights.
    /// </summary>
    private static string FormatInsights(List<RelevanceScore> results)
    {
        if (results.Count == 0)
            return string.Empty;

        var insights = new StringBuilder();
        insights.AppendLine("\n\n---");
        insights.AppendLine("**Related insights from your knowledge base:**\n");

        foreach (var result in results.OrderByDescending(r => r.Score).Take(3))
        {
            // Extract title or filename
            var title = ExtractTitle(result);
            if (!string.IsNullOrWhiteSpace(title))
            {
                insights.AppendLine($"- **{title}** (relevance: {(result.Score * 100):F0}%)");
            }
        }

        return insights.ToString();
    }

    /// <summary>
    /// Extract a clean title from the knowledge result.
    /// </summary>
    private static string ExtractTitle(RelevanceScore result)
    {
        // Try to extract filename from EntryId
        if (!string.IsNullOrWhiteSpace(result.EntryId))
        {
            return Path.GetFileNameWithoutExtension(result.EntryId);
        }

        // Fall back to first 100 chars of content
        return result.Content.Length > 100 
            ? result.Content.Substring(0, 100) + "..." 
            : result.Content;
    }

    /// <summary>
    /// Append knowledge references to the response in a natural way.
    /// </summary>
    private static string AppendKnowledgeReferences(
        string originalResponse,
        string insights)
    {
        return originalResponse + insights;
    }
}
