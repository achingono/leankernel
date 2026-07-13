using System.Security.Claims;
using FluentAssertions;
using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Gateway;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Providers;
using LeanKernel.Gateway.Requests;
using LeanKernel.Gateway.Sessions;
using LeanKernel.Logic.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit;

/// <summary>
/// Tests covering newly added or significantly changed code to satisfy coverage targets.
/// </summary>
public class NewCodeCoverageTests
{
    [Fact]
    public void AgentSettings_DefaultValues_AreCorrect()
    {
        var settings = new AgentSettings();
        settings.RootPath.Should().Be("agents");
        settings.DefaultName.Should().Be("leankernel");
        settings.DefaultInstructions.Should().Be("You are a helpful AI assistant.");
        settings.DefaultDescription.Should().BeEmpty();
    }

    [Fact]
    public void AgentSettings_CanSetProperties()
    {
        var settings = new AgentSettings
        {
            RootPath = "/custom",
            DefaultName = "custom-agent",
            DefaultInstructions = "Custom instructions",
            DefaultDescription = "Custom description"
        };
        settings.RootPath.Should().Be("/custom");
        settings.DefaultName.Should().Be("custom-agent");
        settings.DefaultInstructions.Should().Be("Custom instructions");
        settings.DefaultDescription.Should().Be("Custom description");
    }

    [Theory]
    [InlineData("Postgres", "Host=localhost;Database=test")]
    [InlineData("SqlServer", "Server=localhost;Database=test")]
    [InlineData("Sqlite", "Data Source=test.db")]
    public void ConfigureOptions_SupportedProvider_DoesNotThrow(string name, string connStr)
    {
        var builder = new DbContextOptionsBuilder<EntityContext>();
        var result = builder.ConfigureOptions(name, connStr);
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ConfigureOptions_NullConnectionString_Throws()
    {
        var builder = new DbContextOptionsBuilder<EntityContext>();
        var act = () => builder.ConfigureOptions("Postgres", null);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Connection string is missing*");
    }

    [Fact]
    public void ConfigureOptions_EmptyConnectionString_AllowEmptyTrue_UsesProvider()
    {
        var builder = new DbContextOptionsBuilder<EntityContext>();
        var result = builder.ConfigureOptions("Postgres", "", allowEmptyConnectionString: true);
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ConfigureOptions_NullName_ThrowsWhenNotAllowed()
    {
        var builder = new DbContextOptionsBuilder<EntityContext>();
        var act = () => builder.ConfigureOptions(null, "some-connection-string");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Connection string is missing*");
    }

    [Fact]
    public void ConfigureOptions_UnsupportedName_Throws()
    {
        var builder = new DbContextOptionsBuilder<EntityContext>();
        var act = () => builder.ConfigureOptions("Oracle", "some-connection-string");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unsupported*");
    }

    [Fact]
    public void ConfigureOptions_WithFlags_AppliesFlags()
    {
        var builder = new DbContextOptionsBuilder<EntityContext>();
        var result = builder.ConfigureOptions(
            "Sqlite", "Data Source=:memory:",
            enableDetailedErrors: true,
            enableSensitiveDataLogging: true);
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ConfigureOptions_AllSupportedNames_Work()
    {
        var builder = new DbContextOptionsBuilder<EntityContext>();
        builder.ConfigureOptions("Postgres", "Host=localhost").Should().BeSameAs(builder);
        builder = new DbContextOptionsBuilder<EntityContext>();
        builder.ConfigureOptions("SqlServer", "Server=localhost").Should().BeSameAs(builder);
        builder = new DbContextOptionsBuilder<EntityContext>();
        builder.ConfigureOptions("Sqlite", "Data Source=:memory:").Should().BeSameAs(builder);
    }

    [Fact]
    public void ClaimsPrincipalExtensions_Id_ExtractsSidClaim()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Sid, "12345678-1234-1234-1234-123456789012")]);
        var principal = new ClaimsPrincipal(identity);
        var id = principal.Id();
        id.Should().Be(new Guid("12345678-1234-1234-1234-123456789012"));
    }

    [Fact]
    public void ClaimsPrincipalExtensions_Id_ExtractsNameIdentifierClaim()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "abcdefab-1234-1234-1234-abcdefabcdef")]);
        var principal = new ClaimsPrincipal(identity);
        var id = principal.Id();
        id.Should().Be(new Guid("abcdefab-1234-1234-1234-abcdefabcdef"));
    }

    [Fact]
    public void ClaimsPrincipalExtensions_Id_ExtractsSubClaim()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "abcdefab-1234-1234-1234-abcdefabcdef")]);
        var principal = new ClaimsPrincipal(identity);
        var id = principal.Id();
        id.Should().Be(new Guid("abcdefab-1234-1234-1234-abcdefabcdef"));
    }

    [Fact]
    public void ClaimsPrincipalExtensions_Id_ReturnsEmpty_WhenNoClaims()
    {
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        principal.Id().Should().Be(Guid.Empty);
    }

    [Fact]
    public void ClaimsPrincipalExtensions_Id_ReturnsEmpty_WhenNonGuidValue()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Sid, "not-a-guid")]);
        var principal = new ClaimsPrincipal(identity);
        principal.Id().Should().Be(Guid.Empty);
    }

    [Fact]
    public void ClaimsPrincipalExtensions_Id_EmptyClaimValue_ReturnsEmpty()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Sid, "")]);
        var principal = new ClaimsPrincipal(identity);
        principal.Id().Should().Be(Guid.Empty);
    }

    [Fact]
    public void ClaimsPrincipalExtensions_Id_SidClaim_ExtractsGuid()
    {
        var guid = "12345678-1234-1234-1234-123456789012";
        var identity = new ClaimsIdentity([new Claim("sid", guid)]);
        var principal = new ClaimsPrincipal(identity);
        principal.Id().Should().Be(new Guid(guid));
    }

    [Fact]
    public void RequestContextPermit_HostName_ReturnsFromAccessor()
    {
        var httpAccessor = new Mock<IHttpContextAccessor>();
        var principalAccessor = new Mock<IPrincipalAccessor>();
        var hostAccessor = new Mock<IHostNameAccessor>();
        hostAccessor.Setup(h => h.HostName).Returns("test-host");
        var sp = new Mock<IServiceProvider>();
        var settings = Options.Create(new IdentitySettings());

        var permit = new RequestContextPermit(
            principalAccessor.Object,
            hostAccessor.Object,
            httpAccessor.Object,
            sp.Object,
            settings);

        permit.HostName.Should().Be("test-host");
    }

    [Fact]
    public void RequestContextPermit_IsAuthenticated_WhenNoContext_ReturnsFalse()
    {
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(h => h.HttpContext).Returns((HttpContext?)null);
        var principalAccessor = new Mock<IPrincipalAccessor>();
        var hostAccessor = new Mock<IHostNameAccessor>();
        hostAccessor.Setup(h => h.HostName).Returns("localhost");
        var sp = new Mock<IServiceProvider>();
        var settings = Options.Create(new IdentitySettings());

        var permit = new RequestContextPermit(
            principalAccessor.Object,
            hostAccessor.Object,
            httpAccessor.Object,
            sp.Object,
            settings);

        permit.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void RequestContextPermit_SessionId_ReturnsFromContext()
    {
        var ctx = new DefaultHttpContext();
        var sessionFeature = new TestSessionFeature();
        ctx.Features.Set<ISessionFeature>(sessionFeature);

        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(h => h.HttpContext).Returns(ctx);
        var principalAccessor = new Mock<IPrincipalAccessor>();
        var hostAccessor = new Mock<IHostNameAccessor>();
        hostAccessor.Setup(h => h.HostName).Returns("localhost");
        var sp = new Mock<IServiceProvider>();
        var settings = Options.Create(new IdentitySettings());

        var permit = new RequestContextPermit(
            principalAccessor.Object,
            hostAccessor.Object,
            httpAccessor.Object,
            sp.Object,
            settings);

        permit.SessionId.Should().NotBeNull();
    }

    [Fact]
    public void RequestContextPermit_UserId_ReturnsEmpty_WhenNoHttpContext()
    {
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(h => h.HttpContext).Returns((HttpContext?)null);
        var principalAccessor = new Mock<IPrincipalAccessor>();
        var hostAccessor = new Mock<IHostNameAccessor>();
        hostAccessor.Setup(h => h.HostName).Returns("localhost");
        var sp = new Mock<IServiceProvider>();
        var settings = Options.Create(new IdentitySettings());

        var permit = new RequestContextPermit(
            principalAccessor.Object,
            hostAccessor.Object,
            httpAccessor.Object,
            sp.Object,
            settings);

        permit.UserId.Should().Be(Guid.Empty);
        permit.TenantId.Should().Be(Guid.Empty);
        permit.ChannelId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void RequestContextPermit_Badge_ReturnsDefault_WhenNoHttpContext()
    {
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(h => h.HttpContext).Returns((HttpContext?)null);
        var principalAccessor = new Mock<IPrincipalAccessor>();
        var hostAccessor = new Mock<IHostNameAccessor>();
        hostAccessor.Setup(h => h.HostName).Returns("localhost");
        var sp = new Mock<IServiceProvider>();
        var settings = Options.Create(new IdentitySettings());

        var permit = new RequestContextPermit(
            principalAccessor.Object,
            hostAccessor.Object,
            httpAccessor.Object,
            sp.Object,
            settings);

        var badge = permit.Badge;
        badge.Should().NotBeNull();
        badge.FullName.Should().Be("System");
    }

    [Fact]
    public void RequestContextPermit_ResolveIdempotent_MultipleAccesses()
    {
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(h => h.HttpContext).Returns((HttpContext?)null);
        var principalAccessor = new Mock<IPrincipalAccessor>();
        var hostAccessor = new Mock<IHostNameAccessor>();
        hostAccessor.Setup(h => h.HostName).Returns("localhost");
        var sp = new Mock<IServiceProvider>();
        var settings = Options.Create(new IdentitySettings());

        var permit = new RequestContextPermit(
            principalAccessor.Object,
            hostAccessor.Object,
            httpAccessor.Object,
            sp.Object,
            settings);

        var first = permit.UserId;
        var second = permit.UserId;
        first.Should().Be(second);
    }

    [Fact]
    public async Task DbAgentStateStore_SaveSessionAsync_UpdatesExisting()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new EntityContext(options);
        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(Guid.NewGuid());
        permit.Setup(p => p.UserId).Returns(Guid.NewGuid());
        permit.Setup(p => p.ChannelId).Returns(Guid.NewGuid());

        var store = new DbAgentStateStore(context, permit.Object);
        var agent = CreateStubAgent();
        var convId = $"test-conv-{Guid.NewGuid():N}";
        var session1 = await agent.CreateSessionAsync();
        await store.SaveSessionAsync(agent, convId, session1);
        context.ChangeTracker.Clear();

        var session2 = await agent.CreateSessionAsync();
        await store.SaveSessionAsync(agent, convId, session2);

        var entity = await context.AgentStates.FirstOrDefaultAsync(e => e.ScopedConversationId == convId);
        entity.Should().NotBeNull();
        entity!.TenantId.Should().Be(permit.Object.TenantId);
    }

    [Fact]
    public async Task DbAgentStateStore_GetSessionAsync_InvalidJson_ReturnsNewSession()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new EntityContext(options);
        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(Guid.NewGuid());
        permit.Setup(p => p.UserId).Returns(Guid.NewGuid());
        permit.Setup(p => p.ChannelId).Returns(Guid.NewGuid());

        var convId = $"test-conv-{Guid.NewGuid():N}";
        context.AgentStates.Add(new AgentStateEntity
        {
            ScopedConversationId = convId,
            TenantId = permit.Object.TenantId,
            UserId = permit.Object.UserId,
            ChannelId = permit.Object.ChannelId,
            StateJson = "invalid json {{{",
            CreatedOn = DateTimeOffset.UtcNow,
            UpdatedOn = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var store = new DbAgentStateStore(context, permit.Object);
        var agent = CreateStubAgent();

        var session = await store.GetSessionAsync(agent, convId);
        session.Should().NotBeNull();
    }

    [Fact]
    public async Task DbAgentStateStore_GetSessionAsync_EmptyStateJson_ReturnsNewSession()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new EntityContext(options);
        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(Guid.NewGuid());
        permit.Setup(p => p.UserId).Returns(Guid.NewGuid());
        permit.Setup(p => p.ChannelId).Returns(Guid.NewGuid());

        var convId = $"test-conv-{Guid.NewGuid():N}";
        context.AgentStates.Add(new AgentStateEntity
        {
            ScopedConversationId = convId,
            TenantId = permit.Object.TenantId,
            UserId = permit.Object.UserId,
            ChannelId = permit.Object.ChannelId,
            StateJson = "",
            CreatedOn = DateTimeOffset.UtcNow,
            UpdatedOn = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var store = new DbAgentStateStore(context, permit.Object);
        var agent = CreateStubAgent();

        var session = await store.GetSessionAsync(agent, convId);
        session.Should().NotBeNull();
    }

    [Fact]
    public void IdentitySettings_DefaultValues_AreCorrect()
    {
        var settings = new IdentitySettings();
        settings.AnonymousUserName.Should().NotBeNullOrEmpty();
        settings.AnonymousFullName.Should().NotBeNullOrEmpty();
    }

    private static ChatClientAgent CreateStubAgent()
    {
        return new ChatClientAgent(
            new Mock<IChatClient>().Object,
            new ChatClientAgentOptions(),
            null,
            null);
    }

    internal sealed class TestSessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = new TestSession();
    }

    internal sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool IsAvailable => true;
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
#pragma warning disable CS8767 // Nullability of reference types
        public bool TryGetValue(string key, out byte[] value)
#pragma warning restore CS8767
        {
            if (_store.TryGetValue(key, out var v))
            {
                value = v;
                return true;
            }
            value = Array.Empty<byte>();
            return false;
        }
    }
}
