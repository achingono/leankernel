using System.Security.Cryptography;

namespace LeanKernel.Host.Services.Auth;

/// <summary>
/// API token service — issue, verify, list, revoke bearer tokens.
/// Raw tokens are only available at creation time; only hashes are persisted.
/// </summary>
public sealed class TokenService : ITokenService
{
    private const int TokenByteLength = 32;
    private const string TokenPrefix = "sk-LeanKernel-";

    private readonly IAuthStateStore _store;
    private readonly ILogger<TokenService> _logger;
    private readonly int _defaultExpirationDays;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenService" /> class.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="defaultExpirationDays">The default expiration days.</param>
    public TokenService(IAuthStateStore store, ILogger<TokenService> logger, int defaultExpirationDays = 90)
    {
        _store = store;
        _logger = logger;
        _defaultExpirationDays = defaultExpirationDays;
    }

    /// <summary>
    /// Represents the create async.
    /// </summary>
    public async Task<ApiTokenCreationResult> CreateAsync(
        string name, int? expirationDays = null, CancellationToken ct = default)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var rawToken = TokenPrefix + Convert.ToBase64String(rawBytes).TrimEnd('=');
        var hash = HashToken(rawToken);
        var expiry = expirationDays switch
        {
            0 => (DateTimeOffset?)null, // non-expiring
            null => DateTimeOffset.UtcNow.AddDays(_defaultExpirationDays),
            _ => DateTimeOffset.UtcNow.AddDays(expirationDays.Value)
        };

        var token = new ApiToken
        {
            Id = $"tok_{Guid.NewGuid():N}"[..16],
            Name = name,
            Hash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiry
        };

        var state = await _store.LoadAsync(ct);
        state.Tokens.Add(token);
        await _store.SaveAsync(state, ct);

        _logger.LogInformation("API token created: {TokenName} (expires: {Expires})", name, expiry?.ToString("o") ?? "never");

        return new ApiTokenCreationResult { Token = token, RawToken = rawToken };
    }

    /// <summary>
    /// Executes the verify async operation.
    /// </summary>
    /// <param name="rawToken">The raw token.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<ApiToken?> VerifyAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var hash = HashToken(rawToken);
        var state = await _store.LoadAsync(ct);

        var match = state.Tokens.FirstOrDefault(t =>
            !t.IsRevoked && !t.IsExpired &&
            CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(t.Hash),
                Convert.FromBase64String(hash)));

        if (match is null)
            return null;

        match.LastUsedAt = DateTimeOffset.UtcNow;
        await _store.SaveAsync(state, ct);

        return match;
    }

    /// <summary>
    /// Executes the list async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<IReadOnlyList<ApiToken>> ListAsync(CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        return state.Tokens.AsReadOnly();
    }

    /// <summary>
    /// Executes the revoke async operation.
    /// </summary>
    /// <param name="tokenId">The token id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<bool> RevokeAsync(string tokenId, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        var token = state.Tokens.FirstOrDefault(t => t.Id == tokenId);
        if (token is null)
            return false;

        token.RevokedAt = DateTimeOffset.UtcNow;
        await _store.SaveAsync(state, ct);

        _logger.LogInformation("API token revoked: {TokenName}", token.Name);
        return true;
    }

    /// <summary>
    /// Executes the hash token operation.
    /// </summary>
    /// <param name="rawToken">The raw token.</param>
    /// <returns>The operation result.</returns>
    public static string HashToken(string rawToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
