namespace LeanKernel.Channels.Teams.Clients;

/// <summary>Defines the transport contract for receiving and sending Teams activities.</summary>
public interface ITransportClient
{
    /// <summary>Receives the next inbound activity, resolving its bearer token.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The inbound activity with a resolved bearer token, or <c>null</c> if no activity is available.</returns>
    Task<InboundActivity?> ReceiveAsync(CancellationToken ct);
    /// <summary>Enqueues an inbound activity for processing.</summary>
    /// <param name="activity">The activity to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(InboundActivity activity, CancellationToken ct);
    /// <summary>Sends a reply message to the Teams conversation.</summary>
    /// <param name="inboundActivity">The original inbound activity to reply to.</param>
    /// <param name="text">The reply text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendAsync(InboundActivity inboundActivity, string text, CancellationToken ct);
}