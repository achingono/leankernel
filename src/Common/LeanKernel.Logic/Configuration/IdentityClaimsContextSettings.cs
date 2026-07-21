namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configures how identity claims are persisted and rendered into prompt context.
/// </summary>
public sealed class IdentityClaimsContextSettings
{
    /// <summary>
    /// Enables identity claim persistence and prompt rendering.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Allowlisted custom claim names to persist from incoming principals.
    /// </summary>
    public List<string> AllowedCustomClaims { get; set; } = [];

    /// <summary>
    /// Ordered allowlist of profile fields rendered into prompt context.
    /// </summary>
    public List<string> PromptFields { get; set; } =
    [
        Constants.IdentityContextFields.FullName,
        Constants.IdentityContextFields.Email,
        Constants.IdentityContextFields.PreferredUsername,
        Constants.IdentityContextFields.Locale,
        Constants.IdentityContextFields.TimeZone,
        Constants.IdentityContextFields.Organization,
        Constants.IdentityContextFields.Roles,
        Constants.IdentityContextFields.Groups,
        Constants.IdentityContextFields.CustomClaims
    ];

    /// <summary>
    /// Maximum number of role values stored and rendered.
    /// </summary>
    public int MaxRoles { get; set; } = 10;

    /// <summary>
    /// Maximum number of group values stored and rendered.
    /// </summary>
    public int MaxGroups { get; set; } = 20;

    /// <summary>
    /// Maximum number of values persisted for each allowlisted custom claim.
    /// </summary>
    public int MaxCustomClaimValuesPerClaim { get; set; } = 5;

    /// <summary>
    /// Maximum rendered token estimate for the identity block.
    /// </summary>
    public int MaxPromptTokens { get; set; } = 256;
}