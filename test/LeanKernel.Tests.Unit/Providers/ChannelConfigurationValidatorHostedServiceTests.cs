using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Services.Gateway.Providers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

/// <summary>
/// Unit tests for <see cref="ChannelConfigurationValidatorHostedService"/> covering
/// channel binding validation, memory policy defaults, and policy normalization.
/// </summary>
public class ChannelConfigurationValidatorHostedServiceTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly DbContextOptions<EntityContext> _options;
    private readonly EntityContext _dbContext;

    public ChannelConfigurationValidatorHostedServiceTests()
    {
        _options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite($"DataSource={_dbName}.db")
            .Options;

        _dbContext = new EntityContext(_options);
        _dbContext.Database.EnsureCreated();
        _dbContext.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        var dbFile = $"{_dbName}.db";
        if (File.Exists(dbFile))
        {
            File.Delete(dbFile);
        }
    }

    private ChannelConfigurationValidatorHostedService CreateSut(AgentSettings? agentSettings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<EntityContext>(_dbContext);

        return new ChannelConfigurationValidatorHostedService(
            services.BuildServiceProvider(),
            Options.Create(agentSettings ?? new AgentSettings()),
            Mock.Of<ILogger<ChannelConfigurationValidatorHostedService>>());
    }

    [Fact]
    public async Task StartAsync_WithValidBindings_StartsSuccessfully()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var tenant = new TenantEntity
        {
            Id = tenantId,
            Name = "TestTenant",
            HostName = "test.example.com",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        };
        var channel = new ChannelEntity { Id = channelId, Name = "signal" };
        var user = new UserEntity
        {
            Id = userId,
            Email = "user@test.com",
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        };
        var binding = new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChannelId = channelId,
            Issuer = "signal",
            Subject = "+15551234",
            BearerToken = "valid-token",
        };

        _dbContext.Tenants.Add(tenant);
        _dbContext.Users.Add(user);
        _dbContext.Channels.Add(channel);
        _dbContext.ChannelSenderBindings.Add(binding);
        await _dbContext.SaveChangesAsync();

        var settings = new AgentSettings();
        settings.Channels.MemoryPolicyDefaults.Share = ["*"];
        settings.Channels.MemoryPolicyDefaults.Access = ["*"];

        var service = CreateSut(settings);

        var act = () => service.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_WithInvalidBinding_ThrowsInvalidOperationException()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var binding = new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChannelId = channelId,
            Issuer = "signal",
            Subject = "+15551234",
            BearerToken = "valid-token",
        };

        _dbContext.ChannelSenderBindings.Add(binding);
        await _dbContext.SaveChangesAsync();

        var settings = new AgentSettings();
        settings.Channels.MemoryPolicyDefaults.Share = ["*"];
        settings.Channels.MemoryPolicyDefaults.Access = ["*"];

        var service = CreateSut(settings);

        var act = () => service.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*channel sender bindings*");
    }

    [Fact]
    public async Task StartAsync_WithInvalidPolicyReference_ThrowsInvalidOperationException()
    {
        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var policy = new ChannelMemoryPolicyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelId = channelId,
            ShareList = "invalid-channel",
            AccessList = "*",
        };

        _dbContext.ChannelMemoryPolicies.Add(policy);
        _dbContext.Channels.Add(new ChannelEntity { Id = channelId, Name = "known-channel" });
        await _dbContext.SaveChangesAsync();

        var settings = new AgentSettings();
        settings.Channels.MemoryPolicyDefaults.Share = ["*"];
        settings.Channels.MemoryPolicyDefaults.Access = ["*"];

        var service = CreateSut(settings);

        var act = () => service.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid channel memory policy reference*");
    }

    [Fact]
    public async Task StartAsync_NormalizesPolicyLists()
    {
        var channelId = Guid.NewGuid();
        var policy = new ChannelMemoryPolicyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ChannelId = channelId,
            ShareList = " SIGNAL , signal , * ",
            AccessList = "* , signal",
        };

        _dbContext.ChannelMemoryPolicies.Add(policy);
        _dbContext.Channels.Add(new ChannelEntity { Id = channelId, Name = "signal" });
        await _dbContext.SaveChangesAsync();

        var settings = new AgentSettings();
        settings.Channels.MemoryPolicyDefaults.Share = ["*"];
        settings.Channels.MemoryPolicyDefaults.Access = ["*"];

        var service = CreateSut(settings);

        await service.StartAsync(CancellationToken.None);

        policy.ShareList.Should().Be("*");
        policy.AccessList.Should().Be("*");
    }

    [Fact]
    public async Task StartAsync_WithInvalidTokenInDefaults_Throws()
    {
        _dbContext.Channels.Add(new ChannelEntity { Id = Guid.NewGuid(), Name = "signal" });
        await _dbContext.SaveChangesAsync();

        var settings = new AgentSettings();
        settings.Channels.MemoryPolicyDefaults.Share = ["nonexistent-channel"];
        settings.Channels.MemoryPolicyDefaults.Access = ["*"];

        var service = CreateSut(settings);

        var act = () => service.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid channel memory policy reference*");
    }

    [Fact]
    public async Task StopAsync_ReturnsCompletedTask()
    {
        var service = CreateSut();

        var result = service.StopAsync(CancellationToken.None);

        result.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WithEmptyPolicyNames_NormalizesToKnownChannel()
    {
        var channelId = Guid.NewGuid();
        var policy = new ChannelMemoryPolicyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ChannelId = channelId,
            ShareList = "  ,  , signal",
            AccessList = "signal",
        };

        _dbContext.ChannelMemoryPolicies.Add(policy);
        _dbContext.Channels.Add(new ChannelEntity { Id = channelId, Name = "signal" });
        await _dbContext.SaveChangesAsync();

        var settings = new AgentSettings();
        settings.Channels.MemoryPolicyDefaults.Share = ["*"];
        settings.Channels.MemoryPolicyDefaults.Access = ["*"];

        var service = CreateSut(settings);

        await service.StartAsync(CancellationToken.None);

        policy.ShareList.Should().Be("signal");
    }

    [Fact]
    public async Task StartAsync_NonWildcardPolicyWithValidChannel_PersistsNormalized()
    {
        var channel1 = new ChannelEntity { Id = Guid.NewGuid(), Name = "alpha" };
        var channel2 = new ChannelEntity { Id = Guid.NewGuid(), Name = "beta" };
        var policy = new ChannelMemoryPolicyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ChannelId = channel1.Id,
            ShareList = " BETA , alpha , alpha ",
            AccessList = "alpha,beta",
        };

        _dbContext.Channels.Add(channel1);
        _dbContext.Channels.Add(channel2);
        _dbContext.ChannelMemoryPolicies.Add(policy);
        await _dbContext.SaveChangesAsync();

        var settings = new AgentSettings();
        settings.Channels.MemoryPolicyDefaults.Share = ["*"];
        settings.Channels.MemoryPolicyDefaults.Access = ["*"];

        var service = CreateSut(settings);

        await service.StartAsync(CancellationToken.None);

        policy.ShareList.Should().Be("BETA,alpha");
        policy.AccessList.Should().Be("alpha,beta");
    }

    [Fact]
    public async Task StartAsync_NormalizesWithWhitespaceToken_FailsOnUnknownChannel()
    {
        var channel1 = new ChannelEntity { Id = Guid.NewGuid(), Name = "known" };
        var policy = new ChannelMemoryPolicyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ChannelId = channel1.Id,
            ShareList = "  unknown-channel  , known",
            AccessList = "known",
        };

        _dbContext.Channels.Add(channel1);
        _dbContext.ChannelMemoryPolicies.Add(policy);
        await _dbContext.SaveChangesAsync();

        var settings = new AgentSettings();
        settings.Channels.MemoryPolicyDefaults.Share = ["*"];
        settings.Channels.MemoryPolicyDefaults.Access = ["*"];

        var service = CreateSut(settings);

        var act = () => service.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid channel memory policy reference*");
    }
}