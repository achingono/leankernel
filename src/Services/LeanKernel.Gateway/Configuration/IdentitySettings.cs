namespace LeanKernel.Gateway.Configuration;

/// <summary>
/// Represents the settings used for configuring identity and authentication.
/// </summary>
public class IdentitySettings
{
    /// <summary>
    /// The username to use when the identity of a user cannot be determined.
    /// </summary>
    public string AnonymousUserName { get; set; } = "anonymous";

    /// <summary>
    /// The full name to use when the identity of a user cannot be determined.
    /// </summary>
    public string AnonymousFullName { get; set; } = "Anonymous User";

    /// <summary>
    /// The default authentication scheme, such as "Bearer".
    /// </summary>
    public string AuthenticationScheme { get; set; } = Constants.Http.Headers.Bearer;

    /// <summary>
    /// The type of authentication used, such as "Claims".
    /// </summary>
    public string AuthenticationType { get; set; } = "Claims";

    /// <summary>
    /// The settings used for configuring token validation parameters.
    /// </summary>
    public TokenSettings Token { get; set; } = new TokenSettings();

    /// <summary>
    /// The settings required for OpenID Connect authentication.
    /// </summary>
    public OpenIdSettings OpenId { get; set; } = new OpenIdSettings();
}