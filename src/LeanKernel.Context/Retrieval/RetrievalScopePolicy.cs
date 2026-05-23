using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.Retrieval;

/// <summary>
/// Resolves the effective retrieval scope and policy definition for a request.
/// </summary>
public sealed class RetrievalScopePolicy(IOptions<RetrievalConfig> config, ILogger<RetrievalScopePolicy> logger)
{
    private static readonly string[] ScopeMetadataKeys = ["retrieval_scope", "task_scope", "agent_scope"];

    private readonly RetrievalConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<RetrievalScopePolicy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Resolves the effective retrieval scope name for the supplied message.
    /// </summary>
    /// <param name="message">The current message.</param>
    /// <returns>The resolved scope name.</returns>
    public string ResolveScope(LeanKernelMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (var metadataKey in ScopeMetadataKeys)
        {
            if (TryGetMetadataValue(message.Metadata, metadataKey, out var scopeName))
            {
                _logger.LogDebug("Resolved retrieval scope {ScopeName} from message metadata key {MetadataKey}", scopeName, metadataKey);
                return scopeName;
            }
        }

        return GetConfiguredDefaultScope();
    }

    /// <summary>
    /// Resolves the scope policy for the supplied scope name.
    /// </summary>
    /// <param name="scopeName">The requested scope name.</param>
    /// <returns>The resolved scope policy, or <see langword="null"/> when no configured policy applies.</returns>
    public ScopePolicyDefinition? ResolvePolicy(string? scopeName)
    {
        var normalizedScope = string.IsNullOrWhiteSpace(scopeName)
            ? GetConfiguredDefaultScope()
            : scopeName.Trim();

        var requestedPolicy = _config.ScopePolicies.FirstOrDefault(policy =>
            string.Equals(policy.Name, normalizedScope, StringComparison.OrdinalIgnoreCase));
        if (requestedPolicy is not null)
        {
            return requestedPolicy;
        }

        var defaultScope = GetConfiguredDefaultScope();
        if (!string.Equals(normalizedScope, defaultScope, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Retrieval scope {ScopeName} was not configured. Falling back to default scope {DefaultScope}",
                normalizedScope,
                defaultScope);
        }

        return _config.ScopePolicies.FirstOrDefault(policy =>
            string.Equals(policy.Name, defaultScope, StringComparison.OrdinalIgnoreCase));
    }

    private string GetConfiguredDefaultScope()
        => string.IsNullOrWhiteSpace(_config.DefaultScope)
            ? "global"
            : _config.DefaultScope.Trim();

    private static bool TryGetMetadataValue(
        IReadOnlyDictionary<string, string>? metadata,
        string key,
        out string value)
    {
        value = string.Empty;

        if (metadata is null || !metadata.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        value = rawValue.Trim();
        return true;
    }
}
