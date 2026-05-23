namespace LeanKernel.Agents.Routing;

internal static class ResponseQualityHeuristics
{
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
