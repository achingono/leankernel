namespace LeanKernel.Channels.Signal;

public sealed record MarkdownTextResult(string Text, IReadOnlyList<SignalTextStyle> TextStyles);
