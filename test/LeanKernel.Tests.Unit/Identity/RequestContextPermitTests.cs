using System.Security.Claims;
using FluentAssertions;
using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Identity;
using LeanKernel.Gateway.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

public class RequestContextPermitTests
{
    private static (RequestContextPermit permit, Mock<IPrincipalAccessor> principalAccessor, Mock<IHostNameAccessor> hostAccessor) CreateSut(
        ClaimsPrincipal? principal = null,
        string hostName = "localhost",
        UserEntity? resolvedUser = null,
        TenantEntity? resolvedTenant = null,
        ChannelEntity? resolvedChannel = null)
    {
        var principalAccessor = new Mock<IPrincipalAccessor>();
        principalAccessor.Setup(a => a.Principal).Returns(principal);

        var hostAccessor = new Mock<IHostNameAccessor>();
        hostAccessor.Setup(a => a.HostName).Returns(hostName);

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        httpAccessor.Setup(a => a.HttpContext).Returns(ctx);

        var identityResolver = new Mock<IIdentityResolver>();
        identityResolver.Setup(r => r.ResolveTenantAsync(hostName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedTenant);
        identityResolver.Setup(r => r.ResolveOrCreateUserAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedUser ?? new UserEntity { Id = Guid.NewGuid(), UserName = "test-user" });
        identityResolver.Setup(r => r.ResolveGuestUserAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserEntity { Id = Guid.NewGuid(), UserName = "anonymous", IsGuest = true });
        identityResolver.Setup(r => r.ResolveOrCreateChannelAsync(ChannelEntity.OpenAiHttpName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedChannel ?? new ChannelEntity { Id = Guid.NewGuid(), Name = ChannelEntity.OpenAiHttpName });

        var identitySettings = Options.Create(new IdentitySettings
        {
            AnonymousUserName = "anonymous",
            AnonymousFullName = "Anonymous User"
        });

        var permit = new RequestContextPermit(
            principalAccessor.Object,
            hostAccessor.Object,
            httpAccessor.Object,
            identityResolver.Object,
            identitySettings);

        return (permit, principalAccessor, hostAccessor);
    }

    [Fact]
    public void IsAuthenticated_WhenPrincipalIsNull_ReturnsFalse()
    {
        var (permit, _, _) = CreateSut(principal: null);

        permit.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_WhenPrincipalIsAuthenticated_ReturnsTrue()
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _) = CreateSut(principal: principal);

        permit.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticated_WhenPrincipalIsNotAuthenticated_ReturnsFalse()
    {
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _) = CreateSut(principal: principal);

        permit.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void HostName_ReturnsFromAccessor()
    {
        var (permit, _, _) = CreateSut(hostName: "example.com");

        permit.HostName.Should().Be("example.com");
    }

    [Fact]
    public void Badge_WhenAnonymous_ReturnsAnonymousDefaults()
    {
        var (permit, _, _) = CreateSut(principal: null);

        permit.Badge.Should().NotBeNull();
        permit.Badge.FullName.Should().Be("Anonymous User");
    }

    [Fact]
    public void UserId_WhenAuthenticated_ResolvesFromIdentityResolver()
    {
        var userId = Guid.NewGuid();
        var user = new UserEntity { Id = userId, UserName = "test-user" };
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _) = CreateSut(principal: principal, resolvedUser: user);

        permit.UserId.Should().Be(userId);
    }

    [Fact]
    public void UserId_WhenAnonymous_ResolvesGuestUser()
    {
        var (permit, _, _) = CreateSut(principal: null);

        // Should not be Guid.Empty since a guest user is resolved
        permit.UserId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void TenantId_ResolvesFromHost()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new TenantEntity { Id = tenantId, HostName = "example.com", Name = "Test Tenant" };

        var (permit, _, _) = CreateSut(hostName: "example.com", resolvedTenant: tenant);

        permit.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void ChannelId_ResolvesOpenAiHttpChannel()
    {
        var channelId = Guid.NewGuid();
        var channel = new ChannelEntity { Id = channelId, Name = ChannelEntity.OpenAiHttpName };

        var (permit, _, _) = CreateSut(resolvedChannel: channel);

        permit.ChannelId.Should().Be(channelId);
    }

    [Fact]
    public void Id_ReturnsUserId()
    {
        var userId = Guid.NewGuid();
        var user = new UserEntity { Id = userId, UserName = "test-user" };
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _) = CreateSut(principal: principal, resolvedUser: user);

        IPermit iPermit = permit;
        iPermit.Id.Should().Be(userId);
    }
}
