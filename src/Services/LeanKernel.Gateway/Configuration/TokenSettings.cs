namespace LeanKernel.Gateway.Configuration;

/// <summary>
/// Represents the settings used for configuring token validation parameters.
/// These settings are used to validate JWT tokens issued by the application.
/// </summary>
public class TokenSettings
{
    /// <summary>
    /// The secret key used to sign JWT tokens. This key must be kept secure.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// The issuer of the JWT tokens, typically the application or service name.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// The audience for the JWT tokens, representing the intended recipients.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The expiration timeout (in minutes) for JWT tokens.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// The expiration timeout (in days) for persistent JWT tokens.
    /// </summary>
    public int PersistentTimeoutInDays { get; set; } = 365;
}
