namespace LeanKernel.Host.Services.Auth;

/// <summary>
/// Auth scheme and policy name constants.
/// </summary>
public static class AuthConstants
{
    public const string CookieScheme = "LeanKernelCookie";
    public const string BearerScheme = "LeanKernelBearer";

    public const string PolicyUiAuthenticated = "UiAuthenticated";
    public const string PolicyAdminOnly = "AdminOnly";
    public const string PolicyApiAccess = "ApiAccess";

    public const string RoleAdmin = "admin";
    public const string RoleApiClient = "api_client";

    public const string ClaimSecurityStamp = "LeanKernel:stamp";
}

/// <summary>
/// Represents a stored API token (hash + metadata, never the raw value).
/// </summary>
public sealed class ApiToken
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Hash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow;
    public bool IsValid => !IsRevoked && !IsExpired;
}

/// <summary>
/// Result of creating a new token (includes the raw value exactly once).
/// </summary>
public sealed class ApiTokenCreationResult
{
    public required ApiToken Token { get; init; }
    public required string RawToken { get; init; }
}

/// <summary>
/// Persistent auth state (passcode hash, security stamp, tokens).
/// </summary>
public sealed class AuthStateDocument
{
    public string? PasscodeHash { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public List<ApiToken> Tokens { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? BootstrapTokenHash { get; set; }
}

/// <summary>
/// Manages passcode verification and changes.
/// </summary>
public interface IPasscodeService
{
    bool IsConfigured { get; }
    Task<bool> VerifyAsync(string passcode, CancellationToken ct = default);
    Task SetAsync(string newPasscode, CancellationToken ct = default);
    Task ChangeAsync(string currentPasscode, string newPasscode, CancellationToken ct = default);
}

/// <summary>
/// Manages API token lifecycle (create, verify, list, revoke).
/// </summary>
public interface ITokenService
{
    Task<ApiTokenCreationResult> CreateAsync(string name, int? expirationDays = null, CancellationToken ct = default);
    Task<ApiToken?> VerifyAsync(string rawToken, CancellationToken ct = default);
    Task<IReadOnlyList<ApiToken>> ListAsync(CancellationToken ct = default);
    Task<bool> RevokeAsync(string tokenId, CancellationToken ct = default);
}

/// <summary>
/// Manages the security stamp for session invalidation.
/// </summary>
public interface ISecurityStampService
{
    Task<string> GetStampAsync(CancellationToken ct = default);
    Task<string> RotateStampAsync(CancellationToken ct = default);
    Task<bool> ValidateStampAsync(string stamp, CancellationToken ct = default);
}

/// <summary>
/// Persistence layer for auth state.
/// </summary>
public interface IAuthStateStore
{
    Task<AuthStateDocument> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AuthStateDocument state, CancellationToken ct = default);
}
