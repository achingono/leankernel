namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Defines an interface for routing incoming channel messages to the agent runtime.
/// </summary>
public interface IChannelRouter
{
    /// <summary>
    /// Routes an inbound channel message to the agent runtime.
    /// </summary>
    /// <param name="message">The incoming message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous routing operation.</returns>
    Task RouteInboundAsync(ChannelMessage message, CancellationToken ct = default);
}
