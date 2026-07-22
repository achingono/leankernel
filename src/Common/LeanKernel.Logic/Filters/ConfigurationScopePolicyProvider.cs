namespace LeanKernel.Logic.Filters;

using System.Collections.Concurrent;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Resolves <see cref="EntityScopePolicy"/> from configuration-bound <see cref="EntityScopePolicies"/>
/// with known-entity defaults via <c>PostConfigure</c>.
/// Fails closed for unknown scoped entity types.
/// Logs warnings for type resolution failures.
/// </summary>
public sealed class ConfigurationScopePolicyProvider : IScopePolicyProvider
{
    private readonly ConcurrentDictionary<Type, EntityScopePolicy> _cache = new();
    private readonly EntityScopePolicies _policies;
    private readonly ILogger<ConfigurationScopePolicyProvider>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationScopePolicyProvider"/> class.
    /// </summary>
    /// <param name="policies">The configured entity scope policies.</param>
    /// <param name="logger">Optional logger for reporting type resolution failures.</param>
    public ConfigurationScopePolicyProvider(
        IOptions<EntityScopePolicies> policies,
        ILogger<ConfigurationScopePolicyProvider>? logger = null)
    {
        _policies = policies.Value;
        _logger = logger;
        WarmCache();
    }

    private void WarmCache()
    {
        foreach (var policy in _policies.Policies)
        {
            var type = Type.GetType(policy.EntityType)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == policy.EntityType || t.FullName == policy.EntityType);

            if (type is not null)
            {
                _cache[type] = policy;
            }
            else
            {
                _logger?.LogWarning(
                    "Could not resolve entity type '{EntityType}' configured in Agents:EntityScopePolicies.",
                    policy.EntityType);
            }
        }
    }

    /// <inheritdoc />
    public EntityScopePolicy GetPolicy(Type entityType)
    {
        if (_cache.TryGetValue(entityType, out var policy))
        {
            return policy;
        }

        throw new InvalidOperationException(
            $"No scope policy configured for entity type '{entityType.Name}'. " +
            "Add an EntityScopePolicy entry under Agents:EntityScopePolicies or register a default via PostConfigure.");
    }
}