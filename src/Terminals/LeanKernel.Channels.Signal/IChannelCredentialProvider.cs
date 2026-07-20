namespace LeanKernel.Channels.Signal;

public interface IChannelCredentialProvider
{
    Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct);
}
