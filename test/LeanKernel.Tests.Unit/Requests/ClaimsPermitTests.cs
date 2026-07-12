using System.Security.Claims;
using FluentAssertions;
using LeanKernel.Gateway.Requests;
using Xunit;

namespace LeanKernel.Tests.Unit.Requests;

public class ClaimsPermitTests
{
    [Fact]
    public void UserId_ParsesFromSubClaim()
    {
        var userId = Guid.NewGuid();
        var permit = CreatePermit([
            new Claim("sub", userId.ToString())
        ], isAuthenticated: true);

        permit.UserId.Should().Be(userId);
    }

    [Fact]
    public void Badge_ReadsNameAndEmail()
    {
        var permit = CreatePermit([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "Jane Doe"),
            new Claim(ClaimTypes.Email, "jane@test")
        ], isAuthenticated: true);

        permit.Badge.FullName.Should().Be("Jane Doe");
        permit.Badge.Email.Should().Be("jane@test");
    }

    [Fact]
    public void Can_ReturnsFalse_WhenNotAuthenticated()
    {
        var permit = CreatePermit([], isAuthenticated: false);
        permit.Can(Operation.Read).Should().BeFalse();
    }

    [Fact]
    public void Can_ReturnsTrue_ForAdminRole_OrAdminName()
    {
        var principalByRole = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer", ClaimTypes.Name, ClaimTypes.Role));
        var roleIdentity = (ClaimsIdentity)principalByRole.Identity!;
        roleIdentity.AddClaim(new Claim(ClaimTypes.Role, "Administrators"));
        var host = new HostNameAccessorStub("h");
        var byRole = new ClaimsPermit<object>(principalByRole, host);

        byRole.Can(Operation.Update).Should().BeTrue();

        var byName = CreatePermit([new Claim(ClaimTypes.Name, "Administrator Account")], isAuthenticated: true);
        byName.Can(Operation.Delete).Should().BeTrue();
    }

    [Fact]
    public void Can_UsesRightClaimForEntityOperation()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("right", "Read:String")
        ], "Bearer"));

        var permit = new ClaimsPermit<string>(principal, new HostNameAccessorStub("x"));
        permit.Can(Operation.Read).Should().BeTrue();
        permit.Can(Operation.Create).Should().BeFalse();
    }

    private static ClaimsPermit<object> CreatePermit(IEnumerable<Claim> claims, bool isAuthenticated)
    {
        var identity = new ClaimsIdentity(claims, isAuthenticated ? "Bearer" : string.Empty);
        return new ClaimsPermit<object>(new ClaimsPrincipal(identity), new HostNameAccessorStub("host"));
    }

    private sealed class HostNameAccessorStub(string hostName) : IHostNameAccessor
    {
        public string HostName => hostName;
    }
}
