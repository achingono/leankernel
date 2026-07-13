using System.Security.Claims;
using FluentAssertions;
using LeanKernel.Gateway.Requests;
using Xunit;

namespace LeanKernel.Tests.Unit.Requests;

/// <summary>
/// Covers permit behavior derived from claims principals.
/// </summary>
public class ClaimsPermitTests
{
    /// <summary>
    /// Verifies the subject claim is parsed as the user identifier.
    /// </summary>
    [Fact]
    public void UserId_ParsesFromSubClaim()
    {
        var userId = Guid.NewGuid();
        var permit = CreatePermit([
            new Claim("sub", userId.ToString())
        ], isAuthenticated: true);

        permit.UserId.Should().Be(userId);
    }

    /// <summary>
    /// Verifies badge fields are populated from name and email claims.
    /// </summary>
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

    /// <summary>
    /// Verifies anonymous users cannot perform operations.
    /// </summary>
    [Fact]
    public void Can_ReturnsFalse_WhenNotAuthenticated()
    {
        var permit = CreatePermit([], isAuthenticated: false);
        permit.Can(Operation.Read).Should().BeFalse();
    }

    /// <summary>
    /// Verifies administrator role or naming conventions grant access.
    /// </summary>
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

    /// <summary>
    /// Verifies entity permissions are derived from right claims.
    /// </summary>
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

    /// <summary>
    /// Creates a permit with the supplied claims.
    /// </summary>
    private static ClaimsPermit<object> CreatePermit(IEnumerable<Claim> claims, bool isAuthenticated)
    {
        var identity = new ClaimsIdentity(claims, isAuthenticated ? "Bearer" : string.Empty);
        return new ClaimsPermit<object>(new ClaimsPrincipal(identity), new HostNameAccessorStub("host"));
    }

    /// <summary>
    /// Provides a fixed host name for claims permit tests.
    /// </summary>
    /// <param name="hostName">The host name to return.</param>
    private sealed class HostNameAccessorStub(string hostName) : IHostNameAccessor
    {
        public string HostName => hostName;
    }
}
