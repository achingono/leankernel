using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Archivist.Sessions;
using Xunit;

namespace LeanKernel.Tests.Unit.Archivist;

public class SessionStoreMetadataTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SessionStore _store;

    public SessionStoreMetadataTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_test_{Guid.NewGuid():N}");
        _store = new SessionStore(_tempDir, NullLogger<SessionStore>.Instance);
    }

    [Fact]
    public async Task SetAndGetMetadata_RoundTrips()
    {
        await _store.SetMetadataAsync("sess1", "duration_ms", "42", CancellationToken.None);

        var value = await _store.GetMetadataAsync("sess1", "duration_ms", CancellationToken.None);

        Assert.Equal("42", value);
    }

    [Fact]
    public async Task GetMetadata_NonExistent_ReturnsNull()
    {
        var value = await _store.GetMetadataAsync("sess1", "missing", CancellationToken.None);

        Assert.Null(value);
    }

    [Fact]
    public async Task GetAllMetadata_ReturnsAllKeys()
    {
        await _store.SetMetadataAsync("sess1", "key1", "val1", CancellationToken.None);
        await _store.SetMetadataAsync("sess1", "key2", "val2", CancellationToken.None);

        var all = await _store.GetAllMetadataAsync("sess1", CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.Equal("val1", all["key1"]);
        Assert.Equal("val2", all["key2"]);
    }

    [Fact]
    public async Task SetMetadata_OverwritesExistingKey()
    {
        await _store.SetMetadataAsync("sess1", "key", "old", CancellationToken.None);
        await _store.SetMetadataAsync("sess1", "key", "new", CancellationToken.None);

        var value = await _store.GetMetadataAsync("sess1", "key", CancellationToken.None);

        Assert.Equal("new", value);
    }

    [Fact]
    public async Task ListSessions_ExcludesMetadataFiles()
    {
        // Create a session with history and metadata
        await _store.AppendTurnAsync("sess1", new()
        {
            Role = "user",
            Content = "Hello",
            Timestamp = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        await _store.SetMetadataAsync("sess1", "key", "val", CancellationToken.None);

        var sessions = await _store.ListSessionsAsync(CancellationToken.None);

        Assert.Single(sessions);
        Assert.Equal("sess1", sessions[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
