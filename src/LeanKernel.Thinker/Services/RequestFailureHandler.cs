using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Handles request failures proactively by:
/// 1. Detecting when the agent couldn't fulfill a request
/// 2. Searching knowledge base for relevant insights
/// 3. Suggesting improvements or alternative approaches
/// 4. Building capability gap records for self-improvement
/// 
/// Example: User asks for custom integration → agent detects "tool not found" →
/// service searches docs for available integration patterns and suggests workarounds.
/// </summary>
public sealed class RequestFailureHandler
{
    private readonly IKnowledgeSearchService _knowledge;
    private readonly ILogger<RequestFailureHandler> _logger;

    public RequestFailureHandler(
        IKnowledgeSearchService knowledge,
        ILogger<RequestFailureHandler> logger)
    {
        _knowledge = knowledge;
        _logger = logger;
    }

    /// <summary>
    /// Analyze a failed request and generate an improved response with knowledge fallback.
    /// </summary>
    public async Task<string> HandleFailureAsync(
        string userRequest,
        string originalErrorResponse,
        Exception? exception,
        CancellationToken ct)
    {
        try
        {
            // Classify the failure type
            var failureType = ClassifyFailure(originalErrorResponse, exception);
            _logger.LogInformation("Detected failure type: {FailureType}", failureType);

            // Extract what the user was trying to do
            var requestSummary = ExtractRequestIntent(userRequest);

            // Search for relevant knowledge
            var searchTerms = BuildSearchTerms(requestSummary, failureType);
            var results = await _knowledge.SearchAsync(searchTerms, new[] { "general" }, limit: 5, ct);

            // Build improved response
            var improvedResponse = BuildImprovedResponse(
                originalErrorResponse,
                failureType,
                requestSummary,
                results);

            _logger.LogInformation(
                "Generated improved failure response with {ResultCount} knowledge references",
                results.Count);

            return improvedResponse;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure handler encountered error — returning original response");
            return originalErrorResponse;
        }
    }

    /// <summary>
    /// Classify the type of failure that occurred.
    /// </summary>
    private static string ClassifyFailure(string errorResponse, Exception? exception)
    {
        if (exception is ArgumentException)
            return "invalid_argument";

        if (exception is NotImplementedException || exception is NotSupportedException)
            return "unsupported_operation";

        var lowerError = errorResponse.ToLower();

        if (lowerError.Contains("tool") || lowerError.Contains("skill"))
            return "missing_tool";

        if (lowerError.Contains("permission") || lowerError.Contains("unauthorized"))
            return "access_denied";

        if (lowerError.Contains("not found") || lowerError.Contains("cannot find"))
            return "resource_not_found";

        if (lowerError.Contains("timeout") || lowerError.Contains("took too long"))
            return "timeout";

        if (lowerError.Contains("error") || lowerError.Contains("failed"))
            return "general_error";

        return "unknown";
    }

    /// <summary>
    /// Extract the user's intent from their request.
    /// </summary>
    private static string ExtractRequestIntent(string request)
    {
        // Remove common introductions and keep the core request
        var cleaned = request
            .Replace("can you", "", StringComparison.OrdinalIgnoreCase)
            .Replace("could you", "", StringComparison.OrdinalIgnoreCase)
            .Replace("i want to", "", StringComparison.OrdinalIgnoreCase)
            .Replace("i need to", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return cleaned.Length > 200 ? cleaned.Substring(0, 200) : cleaned;
    }

    /// <summary>
    /// Build search terms based on the request and failure type.
    /// </summary>
    private static string BuildSearchTerms(string requestSummary, string failureType)
    {
        return failureType switch
        {
            "missing_tool" => $"tool integration plugin {requestSummary}",
            "unsupported_operation" => $"alternative approach workaround {requestSummary}",
            "access_denied" => $"permissions authorization access {requestSummary}",
            "resource_not_found" => $"find locate search {requestSummary}",
            "timeout" => $"performance optimization speed {requestSummary}",
            _ => requestSummary
        };
    }

    /// <summary>
    /// Build an improved response that acknowledges the limitation and provides alternatives.
    /// </summary>
    private static string BuildImprovedResponse(
        string originalError,
        string failureType,
        string requestSummary,
        List<RelevanceScore> knowledgeResults)
    {
        var response = new System.Text.StringBuilder();

        // Start with acknowledgment
        response.AppendLine(GetFailureAcknowledgment(failureType, requestSummary));

        // Add knowledge-driven suggestions if available
        if (knowledgeResults.Count > 0)
        {
            response.AppendLine("\n**However, here are some related approaches from your knowledge base:**\n");

            foreach (var result in knowledgeResults.OrderByDescending(r => r.Score).Take(3))
            {
                var title = ExtractDocumentTitle(result);
                response.AppendLine($"- **{title}** ({(result.Score * 100):F0}% relevant)");
            }

            response.AppendLine("\nThese resources might contain patterns or solutions applicable to what you're trying to accomplish.");
        }

        // Add capability gap tracking
        response.AppendLine("\n---");
        response.AppendLine($"*I'm tracking this capability gap for future self-improvement. " +
            $"Type: {failureType}*");

        return response.ToString();
    }

    /// <summary>
    /// Get an appropriate acknowledgment message based on failure type.
    /// </summary>
    private static string GetFailureAcknowledgment(string failureType, string request)
    {
        return failureType switch
        {
            "missing_tool" => $"I don't currently have a tool available to help with: {request}. " +
                "This is a capability I'm tracking for future enhancement.",
            "unsupported_operation" => $"I can't directly perform: {request}, " +
                "but let me suggest some alternative approaches based on what I know.",
            "access_denied" => $"I don't have access to: {request}, " +
                "but here are some related resources that might help.",
            "resource_not_found" => $"I couldn't locate: {request}, " +
                "but here are similar resources that might be useful.",
            "timeout" => $"Your request took too long to process: {request}. " +
                "Let me suggest faster alternatives.",
            _ => $"I encountered an issue with: {request}. " +
                "Here's what I found that might help."
        };
    }

    /// <summary>
    /// Extract a clean title from knowledge result.
    /// </summary>
    private static string ExtractDocumentTitle(RelevanceScore result)
    {
        if (!string.IsNullOrWhiteSpace(result.EntryId))
        {
            return System.IO.Path.GetFileNameWithoutExtension(result.EntryId);
        }

        return result.Content.Length > 100 
            ? result.Content.Substring(0, 100) + "..." 
            : result.Content;
    }
}
