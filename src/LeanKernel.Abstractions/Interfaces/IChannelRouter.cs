namespace LeanKernel.Abstractions.Interfaces;

public interface IChannelRouter
{
    Task RouteInboundAsync(ChannelMessage message, CancellationToken ct = default);
}
