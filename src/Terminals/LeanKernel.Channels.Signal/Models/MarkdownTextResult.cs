namespace LeanKernel.Channels.Signal;

/// <summary>
/// Result of parsing markdown text into plain text with extracted text styles.
/// </summary>
/// <param name="Text">The plain text after markdown delimiter removal.</param>
/// <param name="TextStyles">The text styles parsed from markdown formatting.</param>
public sealed record MarkdownTextResult(string Text, IReadOnlyList<SignalTextStyle> TextStyles);