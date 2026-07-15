namespace LeanKernel.Channels.Teams;

public static class AttachmentParser
{
    public static IReadOnlyList<string> Parse(IReadOnlyList<string>? attachmentUrls)
    {
        if (attachmentUrls is null || attachmentUrls.Count == 0)
            return [];

        return attachmentUrls
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
