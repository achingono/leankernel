namespace LeanKernel.Gateway.Channels;

/// <summary>
/// Routes inbound channel messages to the agent runtime.
/// </summary>
public interface IChannelRouter
{
    /// <summary>
    /// Routes an inbound channel message to the agent runtime.
    /// </summary>
    /// <param name="message">The message to route.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RouteInboundAsync(ChannelMessage message, CancellationToken ct = default);
}
