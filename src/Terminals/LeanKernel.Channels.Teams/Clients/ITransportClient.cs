namespace LeanKernel.Channels.Teams.Clients;

public interface ITransportClient
{
    Task<InboundActivity?> ReceiveAsync(CancellationToken ct);
    Task EnqueueAsync(InboundActivity activity, CancellationToken ct);
    Task SendAsync(InboundActivity inboundActivity, string text, CancellationToken ct);
}