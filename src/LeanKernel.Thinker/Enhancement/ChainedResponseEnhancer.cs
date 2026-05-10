using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Enhancement;

/// <summary>
/// Chains multiple response enhancers together, applying them in sequence.
/// </summary>
public sealed class ChainedResponseEnhancer : IResponseEnhancer
{
    private readonly IReadOnlyList<IResponseEnhancer> _enhancers;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChainedResponseEnhancer" /> class.
    /// </summary>
    /// <param name="enhancers">The enhancers to apply in sequence.</param>
    public ChainedResponseEnhancer(params IResponseEnhancer[] enhancers)
    {
        _enhancers = enhancers;
    }

    /// <inheritdoc />
    public async Task<string> EnhanceResponseAsync(
        string userQuery,
        string assistantResponse,
        ConversationContext context,
        CancellationToken ct)
    {
        var result = assistantResponse;
        foreach (var enhancer in _enhancers)
        {
            result = await enhancer.EnhanceResponseAsync(userQuery, result, context, ct);
        }

        return result;
    }
}
