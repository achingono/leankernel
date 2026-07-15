using FluentAssertions;
using LeanKernel.Logic.Tools.Dynamic;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class EgressValidatorTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("169.254.169.254")]
    public void IsPrivateOrLoopbackHost_BlockedHosts_ReturnsTrue(string host)
    {
        EgressValidator.IsPrivateOrLoopbackHost(host).Should().BeTrue();
    }

    [Theory]
    [InlineData("api.search.brave.com")]
    [InlineData("api.duckduckgo.com")]
    [InlineData("example.com")]
    [InlineData("8.8.8.8")]
    public void IsPrivateOrLoopbackHost_PublicHosts_ReturnsFalse(string host)
    {
        EgressValidator.IsPrivateOrLoopbackHost(host).Should().BeFalse();
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    public void IsPrivateOrLoopbackHost_PrivateIPs_ReturnsTrue(string host)
    {
        EgressValidator.IsPrivateOrLoopbackHost(host).Should().BeTrue();
    }

    [Theory]
    [InlineData("fe80::1")]    // IPv6 link-local
    [InlineData("fc00::1")]    // IPv6 unique-local
    [InlineData("fd12::1")]    // IPv6 unique-local (fd prefix)
    [InlineData("[fe80::1]")]  // IPv6 link-local bracketed
    public void IsPrivateOrLoopbackHost_IPv6PrivateAddresses_ReturnsTrue(string host)
    {
        EgressValidator.IsPrivateOrLoopbackHost(host).Should().BeTrue();
    }

    [Theory]
    [InlineData("2001:db8::1")] // IPv6 documentation range - public
    [InlineData("2600::1")]     // IPv6 public
    public void IsPrivateOrLoopbackHost_IPv6PublicAddresses_ReturnsFalse(string host)
    {
        EgressValidator.IsPrivateOrLoopbackHost(host).Should().BeFalse();
    }

    [Fact]
    public void IsPrivateOrLoopbackHost_EmptyHost_ReturnsTrue()
    {
        EgressValidator.IsPrivateOrLoopbackHost(string.Empty).Should().BeTrue();
    }

    [Fact]
    public void IsHostAllowed_InBothLists_ReturnsTrue()
    {
        var result = EgressValidator.IsHostAllowed(
            "api.example.com",
            ["api.example.com"],
            ["api.example.com", "other.com"]);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsHostAllowed_InSkillListOnly_GlobalListEmpty_ReturnsTrue()
    {
        var result = EgressValidator.IsHostAllowed(
            "api.example.com",
            ["api.example.com"],
            []);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsHostAllowed_InSkillListButNotGlobalList_ReturnsFalse()
    {
        var result = EgressValidator.IsHostAllowed(
            "api.example.com",
            ["api.example.com"],
            ["other.com"]);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsHostAllowed_NotInSkillList_ReturnsFalse()
    {
        var result = EgressValidator.IsHostAllowed(
            "evil.com",
            ["api.example.com"],
            []);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsHostAllowed_LoopbackHost_ReturnsFalse()
    {
        var result = EgressValidator.IsHostAllowed(
            "localhost",
            ["localhost"],
            []);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryValidateEgressTarget_ValidUrl_ReturnsNull()
    {
        var error = EgressValidator.TryValidateEgressTarget(
            "https://api.example.com/v1/search",
            ["api.example.com"],
            []);

        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateEgressTarget_BlockedHost_ReturnsError()
    {
        var error = EgressValidator.TryValidateEgressTarget(
            "http://localhost/api",
            ["localhost"],
            []);

        error.Should().Contain("localhost");
    }

    [Fact]
    public void TryValidateEgressTarget_InvalidUrl_ReturnsError()
    {
        var error = EgressValidator.TryValidateEgressTarget(
            "not-a-url",
            [],
            []);

        error.Should().Contain("Invalid URL");
    }

    [Fact]
    public void TryValidateEgressTarget_NotInAllowlist_ReturnsError()
    {
        var error = EgressValidator.TryValidateEgressTarget(
            "https://unlisted.com/api",
            ["api.example.com"],
            []);

        error.Should().Contain("allowlist");
    }

    [Fact]
    public void TryValidateEgressTarget_UnsupportedScheme_ReturnsError()
    {
        var error = EgressValidator.TryValidateEgressTarget(
            "ftp://api.example.com/file",
            ["api.example.com"],
            []);

        error.Should().Contain("scheme");
    }
}
