namespace LeanKernel.Channels.Signal;

public sealed record GatewayTurnResult(string Text, IReadOnlyList<SignalTextStyle> TextStyles);
