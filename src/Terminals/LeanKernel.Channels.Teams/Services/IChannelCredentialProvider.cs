namespace LeanKernel.Channels.Teams.Services;

public interface IChannelCredentialProvider
{
    Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct);
}
