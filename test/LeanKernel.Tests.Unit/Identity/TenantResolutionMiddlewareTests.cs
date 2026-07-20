using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Providers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

/// <summary>
/// Regression tests for TenantResolutionMiddleware covering C2, M6, and M7 findings.
/// </summary>
public class TenantResolutionMiddlewareTests
{
    private static IOptions<IdentitySettings> DefaultSettings =>
        Options.Create(new IdentitySettings());

    // ─── C2: Fail-closed tenant resolution ───────────────────────────────────

    /// <summary>
    /// C2: Requests from hosts with no matching tenant must be rejected with 401.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task InvokeAsync_UnknownTenant_Returns401()
    {
        var resolver = new Mock<IIdentityResolver>();
        resolver
            .Setup(r => r.ResolveTenantAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantEntity?)null);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/responses";
        ctx.Request.Host = new HostString("unknown.example.com");
        ctx.Response.Body = new System.IO.MemoryStream();

        var nextInvoked = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx, resolver.Object, DefaultSettings);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized,
            because: "requests from unknown tenants must be rejected to prevent ownership confusion");
        nextInvoked.Should().BeFalse("next middleware must not run when tenant is rejected");
    }

    /// <summary>
    /// C2: Inactive tenants must also be rejected with 401.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task InvokeAsync_InactiveTenant_Returns401()
    {
        var inactiveTenant = new TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = "Inactive",
            HostName = "inactive.test",
            IsActive = false
        };
        var resolver = new Mock<IIdentityResolver>();

        // ResolveTenantAsync already filters to active; returning null simulates inactive rejection
        resolver
            .Setup(r => r.ResolveTenantAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantEntity?)null);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/responses";
        ctx.Response.Body = new System.IO.MemoryStream();

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ctx, resolver.Object, DefaultSettings);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    // ─── Health probe bypass ─────────────────────────────────────────────────

    /// <summary>
    /// C2: Health check path must bypass tenant resolution and always reach next.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task InvokeAsync_HealthPath_BypassesTenantResolution()
    {
        var resolver = new Mock<IIdentityResolver>();

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/health";

        var nextInvoked = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx, resolver.Object, DefaultSettings);

        nextInvoked.Should().BeTrue("health probes must never be blocked by tenant resolution");
        resolver.Verify(
            r => r.ResolveTenantAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── C2/M6: Identity stored in HttpContext.Items ─────────────────────────

    /// <summary>
    /// C2/M6: For authenticated users, tenant and user IDs must be stored in HttpContext.Items
    /// so that RequestContextPermit can read them without blocking.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_StoresIdentityInItems()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var resolver = new Mock<IIdentityResolver>();
        resolver
            .Setup(r => r.ResolveTenantAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntity { Id = tenantId, Name = "T", HostName = "t.test", IsActive = true });
        resolver
            .Setup(r => r.ResolveOrCreateUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserEntity { Id = userId, FullName = "Test", Email = "t@t" });
        resolver
            .Setup(r => r.ResolveOrCreateChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelEntity { Id = channelId, Name = "openai-http" });

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/responses";

        // Create an authenticated user
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", "test"), new System.Security.Claims.Claim("iss", "tests")],
            "Bearer");
        ctx.User = new System.Security.Claims.ClaimsPrincipal(identity);

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ctx, resolver.Object, DefaultSettings);

        ctx.Items[TenantResolutionMiddleware.TenantKey].Should().Be(tenantId,
            because: "TenantId must be stored in Items for RequestContextPermit to read");
        ctx.Items[TenantResolutionMiddleware.UserIdKey].Should().Be(userId);
        ctx.Items[TenantResolutionMiddleware.PersonIdKey].Should().Be(userId);
        ctx.Items[TenantResolutionMiddleware.ChannelIdKey].Should().Be(channelId);
    }

    /// <summary>
    /// C2/M6: For anonymous users, tenant and guest user IDs must be stored in HttpContext.Items.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task InvokeAsync_AnonymousUser_StoresGuestIdentityInItems()
    {
        var tenantId = Guid.NewGuid();
        var guestUserId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var resolver = new Mock<IIdentityResolver>();
        resolver
            .Setup(r => r.ResolveTenantAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntity { Id = tenantId, Name = "T", HostName = "t.test", IsActive = true });
        resolver
            .Setup(r => r.ResolveGuestUserAsync(tenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserEntity { Id = guestUserId });
        resolver
            .Setup(r => r.ResolveOrCreateChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelEntity { Id = channelId, Name = "openai-http" });

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/responses";

        // Inject a test session to avoid null reference when middleware calls Session.SetString
        ctx.Features.Set<ISessionFeature>(new TestSessionFeature());

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ctx, resolver.Object, DefaultSettings);

        ctx.Items[TenantResolutionMiddleware.TenantKey].Should().Be(tenantId);
        ctx.Items[TenantResolutionMiddleware.UserIdKey].Should().Be(guestUserId);
        ctx.Items[TenantResolutionMiddleware.PersonIdKey].Should().Be(guestUserId);
        ctx.Items[TenantResolutionMiddleware.ChannelIdKey].Should().Be(channelId);
    }

    [Fact]
    public async Task InvokeAsync_ChannelClaims_ResolvesFromClaimsAndBinding()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var resolver = new Mock<IIdentityResolver>();
        resolver
            .Setup(r => r.ResolveTenantByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntity { Id = tenantId, Name = "Tenant", HostName = "tenant.test", IsActive = true });
        resolver
            .Setup(r => r.ResolveChannelAsync(ChannelEntity.SignalName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelEntity { Id = channelId, Name = ChannelEntity.SignalName });
        resolver
            .Setup(r => r.ResolveUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserEntity { Id = userId, Issuer = "signal", Subject = "+15551234" });
        resolver
            .Setup(r => r.IsChannelSenderBindingActiveAsync(
                tenantId,
                userId,
                channelId,
                "signal",
                "+15551234",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/responses";
        ctx.Request.Host = new HostString("ignored.test");
        var identity = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("lk_tenant_id", tenantId.ToString()),
                new System.Security.Claims.Claim("lk_channel", ChannelEntity.SignalName),
                new System.Security.Claims.Claim("lk_sender_iss", "signal"),
                new System.Security.Claims.Claim("lk_sender_sub", "+15551234")
            ],
            "Bearer");
        ctx.User = new System.Security.Claims.ClaimsPrincipal(identity);

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ctx, resolver.Object, DefaultSettings);

        ctx.Response.StatusCode.Should().NotBe(StatusCodes.Status401Unauthorized);
        ctx.Items[TenantResolutionMiddleware.TenantKey].Should().Be(tenantId);
        ctx.Items[TenantResolutionMiddleware.UserIdKey].Should().Be(userId);
        ctx.Items[TenantResolutionMiddleware.PersonIdKey].Should().Be(userId);
        ctx.Items[TenantResolutionMiddleware.ChannelIdKey].Should().Be(channelId);
    }

    [Fact]
    public async Task InvokeAsync_ChannelClaims_MissingBinding_Returns401()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var resolver = new Mock<IIdentityResolver>();
        resolver
            .Setup(r => r.ResolveTenantByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntity { Id = tenantId, Name = "Tenant", HostName = "tenant.test", IsActive = true });
        resolver
            .Setup(r => r.ResolveChannelAsync(ChannelEntity.SignalName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelEntity { Id = channelId, Name = ChannelEntity.SignalName });
        resolver
            .Setup(r => r.ResolveUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserEntity { Id = userId, Issuer = "signal", Subject = "+15551234" });
        resolver
            .Setup(r => r.IsChannelSenderBindingActiveAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/responses";
        ctx.Request.Host = new HostString("ignored.test");
        var identity = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("lk_tenant_id", tenantId.ToString()),
                new System.Security.Claims.Claim("lk_channel", ChannelEntity.SignalName),
                new System.Security.Claims.Claim("lk_sender_iss", "signal"),
                new System.Security.Claims.Claim("lk_sender_sub", "+15551234")
            ],
            "Bearer");
        ctx.User = new System.Security.Claims.ClaimsPrincipal(identity);

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ctx, resolver.Object, DefaultSettings);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    // ─── Test doubles ─────────────────────────────────────────────────────────
    private sealed class TestSessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = new TestSession();
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _data = [];
        public string Id => "test-session-id";
        public bool IsAvailable => true;
        public IEnumerable<string> Keys => _data.Keys;
        public void Clear() => _data.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _data.Remove(key);
        public void Set(string key, byte[] value) => _data[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _data.TryGetValue(key, out value!);
    }
}