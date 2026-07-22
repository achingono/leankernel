namespace LeanKernel.Logic.Policy;

using LeanKernel.Entities;

/// <summary>
/// Validates that the current channel is in the memory policy's share list.
/// Composes with <see cref="IChannelMemoryPolicyResolver"/> results.
/// ShareList is a comma-separated string of channel names, channel ids, or "*" (wildcard).
/// </summary>
public sealed class MemoryAccessPolicy : IPolicy<ChannelMemoryPolicyEntity>
{
    /// <inheritdoc />
    public string Name => "MemoryAccess";

    /// <inheritdoc />
    public PolicyResult Evaluate(ChannelMemoryPolicyEntity policy, IPolicyContext context)
    {
        if (context.Identity.PersonId == Guid.Empty)
        {
            return PolicyResult.Deny("Person identity is required for memory access.");
        }

        if (policy.ShareList == ChannelEntity.MemoryPolicyWildcard)
        {
            return PolicyResult.Allow();
        }

        var channels = policy.ShareList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (channels.Length == 0)
        {
            return PolicyResult.Allow();
        }

        var currentChannelId = context.Identity.ChannelId.ToString();
        var currentChannelName = context.Metadata.TryGetValue("ChannelName", out var channelNameValue)
            ? channelNameValue as string
            : null;

        var isAllowed = channels.Contains(ChannelEntity.MemoryPolicyWildcard, StringComparer.OrdinalIgnoreCase)
            || channels.Contains(currentChannelId, StringComparer.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(currentChannelName)
                && channels.Contains(currentChannelName, StringComparer.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            return PolicyResult.Deny("Channel is not in the memory share list.");
        }

        return PolicyResult.Allow();
    }
}