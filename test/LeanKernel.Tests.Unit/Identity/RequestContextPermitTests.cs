using System.Security.Claims;
using FluentAssertions;
using LeanKernel.Gateway.Identity;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

public class RequestContextPermitTests
{
    private static (LeanKernel.Gateway.Identity.RequestContextPermit permit, Mock<LeanKernel.Gateway.Requests.IPrincipalAccessor> principalAccessor, Mock<LeanKernel.Gateway.Requests.IHostNameAccessor> hostAccessor, Mock<IHttpContextAccessor> httpAccessor) CreateSut(
        ClaimsPrincipal? principal = null,
        string hostName = "localhost")
    {
        var principalAccessor = new Mock<LeanKernel.Gateway.Requests.IPrincipalAccessor>();
        principalAccessor.Setup(a => a.Principal).Returns(principal);

        var hostAccessor = new Mock<LeanKernel.Gateway.Requests.IHostNameAccessor>();
        hostAccessor.Setup(a => a.HostName).Returns(hostName);

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        httpAccessor.Setup(a => a.HttpContext).Returns(ctx);

        var permit = new LeanKernel.Gateway.Identity.RequestContextPermit(
            principalAccessor.Object,
            hostAccessor.Object,
            httpAccessor.Object);

        return (permit, principalAccessor, hostAccessor, httpAccessor);
    }

    [Fact]
    public void IsAuthenticated_WhenPrincipalIsNull_ReturnsFalse()
    {
        var (permit, _, _, _) = CreateSut(principal: null);

        permit.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_WhenPrincipalIsAuthenticated_ReturnsTrue()
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _, _) = CreateSut(principal: principal);

        permit.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticated_WhenPrincipalIsNotAuthenticated_ReturnsFalse()
    {
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);

        var (permit, _, _, _) = CreateSut(principal: principal);

        permit.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void HostName_ReturnsFromAccessor()
    {
        var (permit, _, _, _) = CreateSut(hostName: "example.com");

        permit.HostName.Should().Be("example.com");
    }

    [Fact]
    public void Badge_WhenAnonymous_ReturnsAnonymousDefaults()
    {
        var (permit, _, _, _) = CreateSut(principal: null);

        permit.Badge.Should().NotBeNull();
        permit.Badge.FullName.Should().Be("Anonymous");
    }

    [Fact]
    public void Id_ReturnsUserId()
    {
        var (permit, _, _, _) = CreateSut();
        permit.UserId = Guid.NewGuid();

        IPermit iPermit = permit;
        iPermit.Id.Should().Be(permit.UserId);
    }
}
