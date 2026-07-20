using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Validates persisted channel bindings and memory policies during startup.
/// </summary>
public sealed class ChannelConfigurationValidatorHostedService(
    IServiceProvider serviceProvider,
    IOptions<AgentSettings> agentSettings,
    ILogger<ChannelConfigurationValidatorHostedService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EntityContext>();

        var knownChannelNames = await dbContext.Channels
            .AsNoTracking()
            .Select(channel => channel.Name)
            .ToListAsync(cancellationToken);

        ValidateDefaults(knownChannelNames);
        await ValidateBindingsAsync(dbContext, cancellationToken);
        await ValidatePoliciesAsync(dbContext, knownChannelNames, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ValidateDefaults(IReadOnlyCollection<string> knownChannelNames)
    {
        var defaults = agentSettings.Value.Channels.MemoryPolicyDefaults;
        ValidateTokens(defaults.Share, knownChannelNames, "Agents:Channels:MemoryPolicyDefaults:Share");
        ValidateTokens(defaults.Access, knownChannelNames, "Agents:Channels:MemoryPolicyDefaults:Access");
    }

    private static async Task ValidateBindingsAsync(EntityContext dbContext, CancellationToken ct)
    {
        var invalidCount = await dbContext.ChannelSenderBindings
            .AsNoTracking()
            .CountAsync(
                binding =>
                !dbContext.Tenants.Any(tenant => tenant.Id == binding.TenantId)
                || !dbContext.Users.Any(user => user.Id == binding.UserId)
                || !dbContext.Channels.Any(channel => channel.Id == binding.ChannelId)
                || string.IsNullOrWhiteSpace(binding.BearerToken),
                ct);

        if (invalidCount > 0)
        {
            throw new InvalidOperationException($"Found {invalidCount} channel sender bindings with unknown tenant/user/channel references.");
        }
    }

    private async Task ValidatePoliciesAsync(EntityContext dbContext, IReadOnlyCollection<string> knownChannelNames, CancellationToken ct)
    {
        var policies = await dbContext.ChannelMemoryPolicies.ToListAsync(ct);
        var changed = false;

        foreach (var policy in policies)
        {
            var share = Normalize(ParseList(policy.ShareList));
            var access = Normalize(ParseList(policy.AccessList));

            ValidateTokens(share, knownChannelNames, $"ChannelMemoryPolicies[{policy.Id}].ShareList");
            ValidateTokens(access, knownChannelNames, $"ChannelMemoryPolicies[{policy.Id}].AccessList");

            var normalizedShare = string.Join(',', share);
            var normalizedAccess = string.Join(',', access);

            if (!string.Equals(policy.ShareList, normalizedShare, StringComparison.Ordinal)
                || !string.Equals(policy.AccessList, normalizedAccess, StringComparison.Ordinal))
            {
                policy.ShareList = normalizedShare;
                policy.AccessList = normalizedAccess;
                policy.UpdatedOn = DateTime.UtcNow;
                changed = true;
            }
        }

        if (changed)
        {
            logger.LogInformation("Normalized wildcard usage for {PolicyCount} channel memory policies.", policies.Count);
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private static void ValidateTokens(IEnumerable<string> values, IReadOnlyCollection<string> knownChannelNames, string source)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, ChannelEntity.MemoryPolicyWildcard, StringComparison.Ordinal))
            {
                continue;
            }

            if (!knownChannelNames.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Invalid channel memory policy reference '{value}' in {source}.");
            }
        }
    }

    private static IReadOnlyList<string> ParseList(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static IReadOnlyList<string> Normalize(IEnumerable<string> values)
    {
        var normalized = values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Contains(ChannelEntity.MemoryPolicyWildcard, StringComparer.OrdinalIgnoreCase))
        {
            return [ChannelEntity.MemoryPolicyWildcard];
        }

        return normalized;
    }
}