namespace LeanKernel.Channels.Signal;

public interface ITransportClient
{
    Task<InboundMessage?> ReceiveAsync(CancellationToken ct);
    Task SendAsync(string account, string recipient, string text, IReadOnlyList<SignalTextStyle> textStyles, CancellationToken ct);
    Task StartTypingAsync(string account, string recipient, CancellationToken ct);
    Task StopTypingAsync(string account, string recipient, CancellationToken ct);
}
