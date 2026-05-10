using System.Text.Json;

namespace LeanKernel.Host.Services.Auth;

/// <summary>
/// File-backed auth state persistence with atomic writes.
/// </summary>
public sealed class AuthStateStore : IAuthStateStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthStateStore" /> class.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    public AuthStateStore(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Executes the load async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<AuthStateDocument> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
                return new AuthStateDocument();

            var json = await File.ReadAllTextAsync(_filePath, ct);
            return JsonSerializer.Deserialize<AuthStateDocument>(json, JsonOptions)
                   ?? new AuthStateDocument();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Executes the save async operation.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SaveAsync(AuthStateDocument state, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            state.UpdatedAt = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(state, JsonOptions);
            var tmpPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }
}
