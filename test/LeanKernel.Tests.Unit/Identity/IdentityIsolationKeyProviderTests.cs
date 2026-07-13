using System.Security.Claims;
using FluentAssertions;
using LeanKernel;
using LeanKernel.Gateway.Providers;
using LeanKernel.Gateway.Requests;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

/// <summary>
/// Covers isolation key generation for identity-scoped sessions.
/// </summary>
public class IdentityIsolationKeyProviderTests
{
    /// <summary>
    /// Creates a permit stub with the supplied identity values.
    /// </summary>
    private static IPermit CreatePermit(
        Guid tenantId,
        Guid userId,
        Guid channelId,
        bool isAuthenticated,
        string? sessionId = null)
    {
        var mock = new Mock<IPermit>();
        mock.Setup(p => p.TenantId).Returns(tenantId);
        mock.Setup(p => p.UserId).Returns(userId);
        mock.Setup(p => p.ChannelId).Returns(channelId);
        mock.Setup(p => p.IsAuthenticated).Returns(isAuthenticated);
        mock.Setup(p => p.SessionId).Returns(sessionId);
        return mock.Object;
    }

    /// <summary>
    /// Verifies authenticated users receive tenant, channel, and user scoping.
    /// </summary>
    [Fact]
    public async Task GetSessionIsolationKey_Authenticated_ReturnsTenantChannelUser()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var permit = CreatePermit(tenantId, userId, channelId, isAuthenticated: true);
        var provider = new IdentityIsolationKeyProvider(permit);

        var key = await provider.GetSessionIsolationKeyAsync();

        key.Should().Be($"{tenantId}|{channelId}|{userId}");
    }

    /// <summary>
    /// Verifies anonymous users include the session identifier in the isolation key.
    /// </summary>
    [Fact]
    public async Task GetSessionIsolationKey_Anonymous_ReturnsTenantChannelUserSession()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var sessionId = "sess-abc-123";
        var permit = CreatePermit(tenantId, userId, channelId, isAuthenticated: false, sessionId: sessionId);
        var provider = new IdentityIsolationKeyProvider(permit);

        var key = await provider.GetSessionIsolationKeyAsync();

        key.Should().Be($"{tenantId}|{channelId}|{userId}|{sessionId}");
    }

    /// <summary>
    /// Verifies anonymous users without a session cannot produce a key.
    /// </summary>
    [Fact]
    public async Task GetSessionIsolationKey_Anonymous_NoSession_Throws()
    {
        var permit = CreatePermit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), isAuthenticated: false, sessionId: null);
        var provider = new IdentityIsolationKeyProvider(permit);

        var act = () => provider.GetSessionIsolationKeyAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Verifies tenant changes produce different isolation keys.
    /// </summary>
    [Fact]
    public async Task GetSessionIsolationKey_DifferentTenants_ProduceDifferentKeys()
    {
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var permitA = CreatePermit(Guid.NewGuid(), userId, channelId, isAuthenticated: true);
        var permitB = CreatePermit(Guid.NewGuid(), userId, channelId, isAuthenticated: true);

        var keyA = await new IdentityIsolationKeyProvider(permitA).GetSessionIsolationKeyAsync();
        var keyB = await new IdentityIsolationKeyProvider(permitB).GetSessionIsolationKeyAsync();

        keyA.Should().NotBe(keyB);
    }

    /// <summary>
    /// Verifies channel changes produce different isolation keys.
    /// </summary>
    [Fact]
    public async Task GetSessionIsolationKey_DifferentChannels_ProduceDifferentKeys()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permitA = CreatePermit(tenantId, userId, Guid.NewGuid(), isAuthenticated: true);
        var permitB = CreatePermit(tenantId, userId, Guid.NewGuid(), isAuthenticated: true);

        var keyA = await new IdentityIsolationKeyProvider(permitA).GetSessionIsolationKeyAsync();
        var keyB = await new IdentityIsolationKeyProvider(permitB).GetSessionIsolationKeyAsync();

        keyA.Should().NotBe(keyB);
    }
}
