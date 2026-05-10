using System.Collections.Concurrent;

namespace LeanKernel.Thinker.Routing;

/// <summary>
/// Thread-safe in-memory tracker for provider / tier cooldown state (FR-5).
/// Marks a tier alias as cooled-down after 429/5xx responses, and clears it
/// automatically after the configured cooldown duration.
/// </summary>
public sealed class ProviderHealthTracker
{
    // Maps alias → cooldown expiry time.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldowns = new();
    private readonly TimeSpan _defaultCooldown;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderHealthTracker" /> class.
    /// </summary>
    /// <param name="defaultCooldown">The default cooldown.</param>
    public ProviderHealthTracker(TimeSpan? defaultCooldown = null)
    {
        _defaultCooldown = defaultCooldown ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Returns <c>true</c> when the alias is currently in cooldown.
    /// </summary>
    public bool IsOnCooldown(string alias)
    {
        if (!_cooldowns.TryGetValue(alias, out var expiry))
            return false;

        if (DateTimeOffset.UtcNow < expiry)
            return true;

        // Expired — remove lazily.
        _cooldowns.TryRemove(alias, out _);
        return false;
    }

    /// <summary>
    /// Marks <paramref name="alias"/> as cooled-down for <see cref="_defaultCooldown"/>.
    /// </summary>
    public void MarkCooledDown(string alias)
    {
        var expiry = DateTimeOffset.UtcNow.Add(_defaultCooldown);
        _cooldowns[alias] = expiry;
    }

    /// <summary>
    /// Returns a snapshot of current cooldown state (keyed by alias → expiry).
    /// Used for FR-7 selection log <c>provider_health_snapshot</c>.
    /// </summary>
    public IReadOnlyDictionary<string, DateTimeOffset> GetSnapshot()
    {
        // Clean expired entries before returning.
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _cooldowns)
        {
            if (now >= kv.Value)
                _cooldowns.TryRemove(kv.Key, out _);
        }

        return new Dictionary<string, DateTimeOffset>(_cooldowns);
    }
}
