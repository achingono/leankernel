namespace LeanKernel.Host.Services.Auth;

/// <summary>
/// Auth scheme and policy name constants.
/// </summary>
public static class AuthConstants
{
    /// <summary>
    /// Represents the cookie scheme.
    /// </summary>
    public const string CookieScheme = "LeanKernelCookie";
    /// <summary>
    /// Represents the bearer scheme.
    /// </summary>
    public const string BearerScheme = "LeanKernelBearer";

    /// <summary>
    /// Represents the policy ui authenticated.
    /// </summary>
    public const string PolicyUiAuthenticated = "UiAuthenticated";
    /// <summary>
    /// Represents the policy admin only.
    /// </summary>
    public const string PolicyAdminOnly = "AdminOnly";
    /// <summary>
    /// Represents the policy api access.
    /// </summary>
    public const string PolicyApiAccess = "ApiAccess";

    /// <summary>
    /// Represents the role admin.
    /// </summary>
    public const string RoleAdmin = "admin";
    /// <summary>
    /// Represents the role api client.
    /// </summary>
    public const string RoleApiClient = "api_client";

    /// <summary>
    /// Represents the claim security stamp.
    /// </summary>
    public const string ClaimSecurityStamp = "LeanKernel:stamp";
}

/// <summary>
/// Represents a stored API token (hash + metadata, never the raw value).
/// </summary>
public sealed class ApiToken
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or sets the hash.
    /// </summary>
    public required string Hash { get; init; }
    /// <summary>
    /// Gets or sets the created at.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>
    /// Gets or sets the last used at.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }
    /// <summary>
    /// Gets or sets the expires at.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>
    /// Gets or sets the revoked at.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>
    /// Gets or sets the is revoked.
    /// </summary>
    public bool IsRevoked => RevokedAt.HasValue;
    /// <summary>
    /// Gets or sets the is expired.
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the is valid.
    /// </summary>
    public bool IsValid => !IsRevoked && !IsExpired;
}

/// <summary>
/// Result of creating a new token (includes the raw value exactly once).
/// </summary>
public sealed class ApiTokenCreationResult
{
    /// <summary>
    /// Gets or sets the token.
    /// </summary>
    public required ApiToken Token { get; init; }
    /// <summary>
    /// Gets or sets the raw token.
    /// </summary>
    public required string RawToken { get; init; }
}

/// <summary>
/// Persistent auth state (passcode hash, security stamp, tokens).
/// </summary>
public sealed class AuthStateDocument
{
    /// <summary>
    /// Gets or sets the passcode hash.
    /// </summary>
    public string? PasscodeHash { get; set; }
    /// <summary>
    /// Gets or sets the security stamp.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>
    /// Gets or sets the tokens.
    /// </summary>
    public List<ApiToken> Tokens { get; set; } = [];
    /// <summary>
    /// Gets or sets the updated at.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the bootstrap token hash.
    /// </summary>
    public string? BootstrapTokenHash { get; set; }
}

/// <summary>
/// Manages passcode verification and changes.
/// </summary>
public interface IPasscodeService
{
    /// <summary>
    /// Defines the contract for is configured.
    /// </summary>
    bool IsConfigured { get; }
    /// <summary>
    /// Verifies async information.
    /// </summary>
    Task<bool> VerifyAsync(string passcode, CancellationToken ct = default);
    /// <summary>
    /// Sets async information.
    /// </summary>
    Task SetAsync(string newPasscode, CancellationToken ct = default);
    /// <summary>
    /// Gets or performs the change async operation.
    /// </summary>
    Task ChangeAsync(string currentPasscode, string newPasscode, CancellationToken ct = default);
}

/// <summary>
/// Manages API token lifecycle (create, verify, list, revoke).
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Gets or performs the create async operation.
    /// </summary>
    Task<ApiTokenCreationResult> CreateAsync(string name, int? expirationDays = null, CancellationToken ct = default);
    /// <summary>
    /// Verifies async information.
    /// </summary>
    Task<ApiToken?> VerifyAsync(string rawToken, CancellationToken ct = default);
    /// <summary>
    /// Lists async information.
    /// </summary>
    Task<IReadOnlyList<ApiToken>> ListAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets or performs the revoke async operation.
    /// </summary>
    Task<bool> RevokeAsync(string tokenId, CancellationToken ct = default);
}

/// <summary>
/// Manages the security stamp for session invalidation.
/// </summary>
public interface ISecurityStampService
{
    /// <summary>
    /// Gets stamp async information.
    /// </summary>
    Task<string> GetStampAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets or performs the rotate stamp async operation.
    /// </summary>
    Task<string> RotateStampAsync(CancellationToken ct = default);
    /// <summary>
    /// Validates stamp async information.
    /// </summary>
    Task<bool> ValidateStampAsync(string stamp, CancellationToken ct = default);
}

/// <summary>
/// Persistence layer for auth state.
/// </summary>
public interface IAuthStateStore
{
    /// <summary>
    /// Loads async information.
    /// </summary>
    Task<AuthStateDocument> LoadAsync(CancellationToken ct = default);
    /// <summary>
    /// Saves async information.
    /// </summary>
    Task SaveAsync(AuthStateDocument state, CancellationToken ct = default);
}
