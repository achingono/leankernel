namespace LeanKernel.Entities;

/// <summary>
/// Defines a contract for generating security tokens for authenticated users.
/// </summary>
public interface ISecurityTokenGenerator
{
    /// <summary>
    /// Generates a security token for the specified user, with an option to make it persistent.
    /// </summary>
    /// <param name="sender">The channel sender binding entity for whom to generate a token.</param>
    /// <param name="isPersistent">A value indicating whether the token should be persistent.</param>
    /// <returns>The generated security token.</returns>
    string GenerateToken(ChannelSenderBindingEntity sender, bool isPersistent);
}