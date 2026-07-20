namespace LeanKernel.Channels.Teams;

/// <summary>Parses and deduplicates a list of attachment URLs.</summary>
public static class AttachmentParser
{
    /// <summary>Parses, validates, and deduplicates attachment URLs.</summary>
    /// <param name="attachmentUrls">The raw attachment URLs to parse.</param>
    /// <returns>A deduplicated list of valid absolute URLs.</returns>
    public static IReadOnlyList<string> Parse(IReadOnlyList<string>? attachmentUrls)
    {
        if (attachmentUrls is null || attachmentUrls.Count == 0)
        {
            return [];
        }

        return attachmentUrls
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}