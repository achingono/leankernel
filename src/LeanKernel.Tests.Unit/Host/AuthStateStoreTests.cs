using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Tests.Unit.Host;

public sealed class AuthStateStoreTests
{
    [Fact]
    public async Task LoadAsync_NoFile_ReturnsEmptyState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auth-test-{Guid.NewGuid()}.json");
        try
        {
            var store = new AuthStateStore(path);
            var state = await store.LoadAsync();

            Assert.NotNull(state);
            Assert.Null(state.PasscodeHash);
            Assert.NotEmpty(state.SecurityStamp);
            Assert.Empty(state.Tokens);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auth-test-{Guid.NewGuid()}.json");
        try
        {
            var store = new AuthStateStore(path);
            var original = new AuthStateDocument
            {
                PasscodeHash = "200000.abc.def",
                SecurityStamp = "test-stamp-123",
                Tokens = [new ApiToken
                {
                    Id = "tok_test1",
                    Name = "Test Token",
                    Hash = "hash123",
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
                }]
            };

            await store.SaveAsync(original);
            var loaded = await store.LoadAsync();

            Assert.Equal("200000.abc.def", loaded.PasscodeHash);
            Assert.Equal("test-stamp-123", loaded.SecurityStamp);
            Assert.Single(loaded.Tokens);
            Assert.Equal("tok_test1", loaded.Tokens[0].Id);
            Assert.Equal("Test Token", loaded.Tokens[0].Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAsync_AtomicWrite_CreatesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auth-test-{Guid.NewGuid()}.json");
        try
        {
            var store = new AuthStateStore(path);
            await store.SaveAsync(new AuthStateDocument());

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
