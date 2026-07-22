namespace LeanKernel.Logic.Policy;

using LeanKernel.Entities;

/// <summary>
/// Validates that guest users are not linked to a different person identity.
/// Runs above the repository layer as a domain policy.
/// </summary>
public sealed class IdentityLinkingPolicy : IPolicy<UserEntity>
{
    /// <inheritdoc />
    public string Name => "IdentityLinking";

    /// <inheritdoc />
    public PolicyResult Evaluate(UserEntity user, IPolicyContext context)
    {
        if (user.IsGuest && user.PersonId != user.Id)
        {
            return PolicyResult.Deny("Guest users must not be linked to a different person.");
        }

        return PolicyResult.Allow();
    }
}