namespace LeanKernel.Channels.Signal;

/// <summary>
/// Result of a gateway agent turn containing the response text and associated text styles.
/// </summary>
/// <param name="Text">The response text from the gateway agent.</param>
/// <param name="TextStyles">The text styles to apply to the response text.</param>
public sealed record GatewayTurnResult(string Text, IReadOnlyList<SignalTextStyle> TextStyles);
