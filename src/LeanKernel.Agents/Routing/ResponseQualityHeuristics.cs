namespace LeanKernel.Agents.Routing;

/// <summary>
/// Provides functionality for response quality heuristics.
/// </summary>
internal static class ResponseQualityHeuristics
{
    /// <summary>
    /// Executes looks like refusal.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="refusalPatterns">The refusal patterns.</param>
    /// <returns>The operation result.</returns>
    public static bool LooksLikeRefusal(string response, IReadOnlyList<string> refusalPatterns)
    {
        if (string.IsNullOrWhiteSpace(response) || refusalPatterns.Count == 0)
        {
            return false;
        }

        return refusalPatterns.Any(pattern =>
            !string.IsNullOrWhiteSpace(pattern)
            && response.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
