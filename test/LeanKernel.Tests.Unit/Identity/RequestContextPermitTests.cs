using System.Security.Claims;
using FluentAssertions;
using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Gateway.Providers;
using LeanKernel.Gateway.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

/// <summary>
/// Covers request-scoped permit behavior.
/// </summary>
public class RequestContextPermitTests
{
    /// <summary>
    /// Creates a permit with configurable request and identity state.
    /// </summary>
    private static (RequestContextPermit permit, Mock<IPrincipalAccessor> principalAccessor, Mock<IHostNameAccessor> hostAccessor) CreateSut(
        ClaimsPrincipal? principal = null,
        string hostName = "localhost",
        Guid? resolvedUserId = null,
        Guid? resolvedPersonId = null,
        Guid? resolvedTenantId = null,
        Guid? resolvedChannelId = null,
        Badge? resolvedBadge = null)
    {
        var principalAccessor = new Mock<IPrincipalAccessor>();
        principalAccessor.Setup(a => a.Principal).Returns(principal);

        var hostAccessor = new Mock<IHostNameAccessor>();
        hostAccessor.Setup(a => a.HostName).Returns(hostName);

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<ISessionFeature>(new SessionFeature { Session = new TestSession() });

        // Pre-populate HttpContext.Items as the middleware would (new architecture).
        if (resolvedTenantId.HasValue) ctx.Items[TenantResolutionMiddleware.TenantKey] = resolvedTenantId.Value;
        if (resolvedUserId.HasValue) ctx.Items[TenantResolutionMiddleware.UserIdKey] = resolvedUserId.Value;
        if (resolvedPersonId.HasValue) ctx.Items[TenantResolutionMiddleware.PersonIdKey] = resolvedPersonId.Value;
        if (resolvedChannelId.HasValue) ctx.Items[TenantResolutionMiddleware.ChannelIdKey] = resolvedChannelId.Value;
        if (resolvedBadge is not null) ctx.Items[TenantResolutionMiddleware.BadgeKey] = resolvedBadge;

        httpAccessor.Setup(a => a.HttpContext).Returns(ctx);

        var permit = new RequestContextPermit(
            principalAccessor.Object,
            hostAccessor.Object,
            httpAccessor.Object);

        return (permit, principalAccessor, hostAccessor);
    }

    /// <summary>
    /// Verifies missing principals are treated as anonymous.
    /// </summary>
    [Fact]
    public void IsAuthenticated_WhenPrincipalIsNull_ReturnsFalse()
    {
        var (permit, _, _) = CreateSut(principal: null);

        permit.IsAuthenticated.Should().BeFalse();
    }

    /// <summary>
    /// Verifies authenticated principals are reported as authenticated.
    /// </summary>
    [Fact]
    public void IsAuthenticated_WhenPrincipalIsAuthenticated_ReturnsTrue()
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _) = CreateSut(principal: principal);

        permit.IsAuthenticated.Should().BeTrue();
    }

    /// <summary>
    /// Verifies unauthenticated principals are reported as anonymous.
    /// </summary>
    [Fact]
    public void IsAuthenticated_WhenPrincipalIsNotAuthenticated_ReturnsFalse()
    {
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _) = CreateSut(principal: principal);

        permit.IsAuthenticated.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the host name is sourced from the accessor.
    /// </summary>
    [Fact]
    public void HostName_ReturnsFromAccessor()
    {
        var (permit, _, _) = CreateSut(hostName: "example.com");

        permit.HostName.Should().Be("example.com");
    }

    /// <summary>
    /// Verifies anonymous badges use the configured defaults.
    /// </summary>
    [Fact]
    public void Badge_WhenAnonymous_ReturnsAnonymousDefaults()
    {
        var badge = new Badge { Id = Guid.NewGuid(), FullName = "Anonymous User", Email = "" };
        var (permit, _, _) = CreateSut(principal: null, resolvedBadge: badge);

        permit.Badge.Should().NotBeNull();
        permit.Badge.FullName.Should().Be("Anonymous User");
    }

    /// <summary>
    /// Verifies authenticated users resolve their persisted identifier.
    /// </summary>
    [Fact]
    public void UserId_WhenAuthenticated_ResolvesFromIdentityResolver()
    {
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _) = CreateSut(principal: principal, resolvedUserId: userId);

        permit.UserId.Should().Be(userId);
    }

    [Fact]
    public void PersonId_WhenResolved_UsesPersonContext()
    {
        var personId = Guid.NewGuid();
        var (permit, _, _) = CreateSut(resolvedPersonId: personId, resolvedUserId: Guid.NewGuid());

        permit.PersonId.Should().Be(personId);
    }

    /// <summary>
    /// Verifies anonymous requests resolve a guest user identifier.
    /// </summary>
    [Fact]
    public void UserId_WhenAnonymous_ResolvesGuestUser()
    {
        var guestId = Guid.NewGuid();
        var (permit, _, _) = CreateSut(principal: null, resolvedUserId: guestId);

        // Should not be Guid.Empty since a guest user is resolved
        permit.UserId.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// Verifies tenant resolution is based on the request host.
    /// </summary>
    [Fact]
    public void TenantId_ResolvesFromHost()
    {
        var tenantId = Guid.NewGuid();
        var (permit, _, _) = CreateSut(hostName: "example.com", resolvedTenantId: tenantId);

        permit.TenantId.Should().Be(tenantId);
    }

    /// <summary>
    /// Verifies the OpenAI HTTP channel is resolved for requests.
    /// </summary>
    [Fact]
    public void ChannelId_ResolvesOpenAiHttpChannel()
    {
        var channelId = Guid.NewGuid();
        var (permit, _, _) = CreateSut(resolvedChannelId: channelId);

        permit.ChannelId.Should().Be(channelId);
    }

    /// <summary>
    /// Verifies the permit identifier matches the resolved user identifier.
    /// </summary>
    [Fact]
    public void Id_ReturnsUserId()
    {
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _) = CreateSut(principal: principal, resolvedUserId: userId);

        IPermit iPermit = permit;
        iPermit.Id.Should().Be(userId);
    }

    /// <summary>
    /// Minimal ISession implementation for test contexts.
    /// </summary>
    private sealed class TestSession : ISession
    {
        public string Id => "test-session";
        public bool IsAvailable => true;
        public IEnumerable<string> Keys => [];
        public void Clear() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public void Set(string key, byte[] value) { }
        public bool TryGetValue(string key, out byte[] value)
        {
            value = [];
            return false;
        }
    }

    /// <summary>
    /// Provides session features for test HTTP contexts.
    /// </summary>
    private sealed class SessionFeature : ISessionFeature
    {
        public required ISession Session { get; set; }
    }
}
