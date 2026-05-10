namespace LeanKernel.Host.Services.Auth;

/// <summary>
/// Manages the security stamp — a random value that invalidates all sessions when rotated.
/// </summary>
public sealed class SecurityStampService : ISecurityStampService
{
    private readonly IAuthStateStore _store;
    private readonly ILogger<SecurityStampService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityStampService" /> class.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="logger">The logger.</param>
    public SecurityStampService(IAuthStateStore store, ILogger<SecurityStampService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Executes the get stamp async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<string> GetStampAsync(CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        return state.SecurityStamp;
    }

    /// <summary>
    /// Executes the rotate stamp async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<string> RotateStampAsync(CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        state.SecurityStamp = Guid.NewGuid().ToString("N");
        await _store.SaveAsync(state, ct);
        _logger.LogWarning("Security stamp rotated — all existing sessions invalidated");
        return state.SecurityStamp;
    }

    /// <summary>
    /// Executes the validate stamp async operation.
    /// </summary>
    /// <param name="stamp">The stamp.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<bool> ValidateStampAsync(string stamp, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        return string.Equals(state.SecurityStamp, stamp, StringComparison.Ordinal);
    }
}
