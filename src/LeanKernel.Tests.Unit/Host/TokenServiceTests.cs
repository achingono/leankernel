using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Tests.Unit.Host;

public sealed class TokenServiceTests
{
    [Fact]
    public void HashToken_ProducesConsistentHash()
    {
        var hash1 = TokenService.HashToken("sk-LeanKernel-test-token");
        var hash2 = TokenService.HashToken("sk-LeanKernel-test-token");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashToken_DifferentTokens_DifferentHashes()
    {
        var hash1 = TokenService.HashToken("sk-LeanKernel-token-a");
        var hash2 = TokenService.HashToken("sk-LeanKernel-token-b");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task CreateAsync_ReturnsTokenWithRawValue()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenService>();
        var service = new TokenService(store, logger, 90);

        var result = await service.CreateAsync("test-token");

        Assert.NotNull(result.RawToken);
        Assert.StartsWith("sk-LeanKernel-", result.RawToken);
        Assert.NotEmpty(result.Token.Id);
        Assert.Equal("test-token", result.Token.Name);
        Assert.NotNull(result.Token.ExpiresAt);
        Assert.True(result.Token.IsValid);
    }

    [Fact]
    public async Task CreateAsync_NonExpiring_HasNullExpiry()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenService>();
        var service = new TokenService(store, logger, 90);

        var result = await service.CreateAsync("forever-token", expirationDays: 0);

        Assert.Null(result.Token.ExpiresAt);
        Assert.True(result.Token.IsValid);
    }

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsToken()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenService>();
        var service = new TokenService(store, logger, 90);

        var created = await service.CreateAsync("my-token");
        var verified = await service.VerifyAsync(created.RawToken);

        Assert.NotNull(verified);
        Assert.Equal(created.Token.Id, verified.Id);
    }

    [Fact]
    public async Task VerifyAsync_InvalidToken_ReturnsNull()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenService>();
        var service = new TokenService(store, logger, 90);

        var result = await service.VerifyAsync("sk-LeanKernel-totally-fake");
        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeAsync_ValidId_RevokesToken()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenService>();
        var service = new TokenService(store, logger, 90);

        var created = await service.CreateAsync("revocable");
        var revoked = await service.RevokeAsync(created.Token.Id);

        Assert.True(revoked);

        // Token should no longer verify
        var verified = await service.VerifyAsync(created.RawToken);
        Assert.Null(verified);
    }

    [Fact]
    public async Task RevokeAsync_InvalidId_ReturnsFalse()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenService>();
        var service = new TokenService(store, logger, 90);

        var result = await service.RevokeAsync("tok_nonexistent");
        Assert.False(result);
    }

    [Fact]
    public async Task ListAsync_ReturnsAllTokens()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenService>();
        var service = new TokenService(store, logger, 90);

        await service.CreateAsync("token-1");
        await service.CreateAsync("token-2");

        var list = await service.ListAsync();
        Assert.Equal(2, list.Count);
    }
}

/// <summary>
/// In-memory implementation of IAuthStateStore for testing.
/// </summary>
internal sealed class InMemoryAuthStateStore : IAuthStateStore
{
    private AuthStateDocument _state = new();

    public Task<AuthStateDocument> LoadAsync(CancellationToken ct = default)
        => Task.FromResult(_state);

    public Task SaveAsync(AuthStateDocument state, CancellationToken ct = default)
    {
        _state = state;
        return Task.CompletedTask;
    }
}
