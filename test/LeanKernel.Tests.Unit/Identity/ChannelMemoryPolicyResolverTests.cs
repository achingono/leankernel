using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using LeanKernel.Tests.Unit.TestDoubles;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

public class ChannelMemoryPolicyResolverTests
{
    [Fact]
    public async Task ResolveAsync_DefaultWildcard_AllowsAllChannels()
    {
        var resolver = CreateResolver(out var db, options =>
        {
            options.Channels.MemoryPolicyDefaults.Share = ["*"];
            options.Channels.MemoryPolicyDefaults.Access = ["*"];
        });

        var tenantId = Guid.NewGuid();
        var openAi = new ChannelEntity { Id = Guid.NewGuid(), Name = ChannelEntity.OpenAiHttpName };
        var signal = new ChannelEntity { Id = Guid.NewGuid(), Name = ChannelEntity.SignalName };
        db.Channels.AddRange(openAi, signal);
        await db.SaveChangesAsync();

        var resolution = await resolver.ResolveAsync(tenantId, openAi.Id);

        resolution.ReadableChannelIds.Should().BeEquivalentTo([openAi.Id, signal.Id]);
        resolution.MutuallyVisibleChannelIds.Should().BeEquivalentTo([openAi.Id, signal.Id]);
    }

    [Fact]
    public async Task ResolveAsync_DirectionalIntersection_RequiresShareAndAccess()
    {
        var resolver = CreateResolver(out var db, options =>
        {
            options.Channels.MemoryPolicyDefaults.Share = [];
            options.Channels.MemoryPolicyDefaults.Access = [];
        });

        var tenantId = Guid.NewGuid();
        var openAi = new ChannelEntity { Id = Guid.NewGuid(), Name = ChannelEntity.OpenAiHttpName };
        var signal = new ChannelEntity { Id = Guid.NewGuid(), Name = ChannelEntity.SignalName };
        var teams = new ChannelEntity { Id = Guid.NewGuid(), Name = ChannelEntity.TeamsName };
        db.Channels.AddRange(openAi, signal, teams);
        db.ChannelMemoryPolicies.AddRange(
            new ChannelMemoryPolicyEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ChannelId = openAi.Id,
                ShareList = ChannelEntity.SignalName,
                AccessList = ChannelEntity.SignalName
            },
            new ChannelMemoryPolicyEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ChannelId = signal.Id,
                ShareList = ChannelEntity.OpenAiHttpName,
                AccessList = ChannelEntity.OpenAiHttpName
            },
            new ChannelMemoryPolicyEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ChannelId = teams.Id,
                ShareList = string.Empty,
                AccessList = string.Empty
            });
        await db.SaveChangesAsync();

        var resolution = await resolver.ResolveAsync(tenantId, openAi.Id);

        resolution.ReadableChannelIds.Should().BeEquivalentTo([openAi.Id, signal.Id]);
        resolution.ReadableChannelIds.Should().NotContain(teams.Id);
        resolution.MutuallyVisibleChannelIds.Should().BeEquivalentTo([openAi.Id, signal.Id]);
    }

    private static ChannelMemoryPolicyResolver CreateResolver(out EntityContext db, Action<AgentSettings> configure)
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new EntityContext(options);

        var agentSettings = new AgentSettings();
        configure(agentSettings);

        return new ChannelMemoryPolicyResolver(
            new TestDbContextFactory(options),
            Options.Create(agentSettings));
    }
}