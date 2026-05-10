using System.Text.Json;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Sessions;

/// <summary>
/// File-backed session store. Each session is a JSON file under data/sessions/.
/// </summary>
public sealed class SessionStore : ISessionStore
{
    private readonly string _basePath;
    private readonly ILogger<SessionStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionStore" /> class.
    /// </summary>
    /// <param name="sessionsPath">The sessions path.</param>
    /// <param name="logger">The logger.</param>
    public SessionStore(string sessionsPath, ILogger<SessionStore> logger)
    {
        _basePath = sessionsPath;
        _logger = logger;
        Directory.CreateDirectory(_basePath);
    }

    /// <summary>
    /// Executes the get history async operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<List<ConversationTurn>> GetHistoryAsync(string sessionId, CancellationToken ct)
    {
        var path = GetSessionPath(sessionId);
        if (!File.Exists(path)) return [];

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ConversationTurn>>(stream, JsonOptions, ct) ?? [];
    }

    /// <summary>
    /// Executes the append turn async operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="turn">The turn.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct)
    {
        var history = await GetHistoryAsync(sessionId, ct);
        history.Add(turn);

        var path = GetSessionPath(sessionId);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, history, JsonOptions, ct);
    }

    /// <summary>
    /// Executes the get or create session id async operation.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <param name="senderId">The sender id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public Task<string> GetOrCreateSessionIdAsync(string channelId, string senderId, CancellationToken ct)
    {
        // Deterministic session ID from channel + sender
        var sessionId = $"{channelId}_{senderId}";
        return Task.FromResult(sessionId);
    }

    /// <summary>
    /// Executes the compact async operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task CompactAsync(string sessionId, CancellationToken ct)
    {
        var history = await GetHistoryAsync(sessionId, ct);
        if (history.Count <= 15) return;

        // Archive turns older than 15 — keep only summaries
        var compacted = history
            .TakeLast(15)
            .ToList();

        _logger.LogInformation("Compacted session {SessionId}: {Before} → {After} turns",
            sessionId, history.Count, compacted.Count);

        var path = GetSessionPath(sessionId);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, compacted, JsonOptions, ct);
    }

    /// <summary>
    /// Executes the list sessions async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public Task<IReadOnlyList<string>> ListSessionsAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_basePath))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var sessions = Directory.GetFiles(_basePath, "*.json")
            .Where(f => !f.EndsWith(".meta.json"))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderDescending()
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(sessions);
    }

    /// <summary>
    /// Executes the set metadata async operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SetMetadataAsync(string sessionId, string key, string value, CancellationToken ct)
    {
        var meta = await LoadMetadataAsync(sessionId, ct);
        meta[key] = value;
        await SaveMetadataAsync(sessionId, meta, ct);
    }

    /// <summary>
    /// Executes the get metadata async operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="key">The key.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<string?> GetMetadataAsync(string sessionId, string key, CancellationToken ct)
    {
        var meta = await LoadMetadataAsync(sessionId, ct);
        return meta.GetValueOrDefault(key);
    }

    /// <summary>
    /// Executes the get all metadata async operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetAllMetadataAsync(string sessionId, CancellationToken ct)
    {
        return await LoadMetadataAsync(sessionId, ct);
    }

    private async Task<Dictionary<string, string>> LoadMetadataAsync(string sessionId, CancellationToken ct)
    {
        var path = GetMetadataPath(sessionId);
        if (!File.Exists(path)) return new Dictionary<string, string>();

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, ct)
            ?? new Dictionary<string, string>();
    }

    private async Task SaveMetadataAsync(string sessionId, Dictionary<string, string> meta, CancellationToken ct)
    {
        var path = GetMetadataPath(sessionId);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, meta, JsonOptions, ct);
    }

    private string GetSessionPath(string sessionId) =>
        Path.Combine(_basePath, $"{sessionId}.json");

    private string GetMetadataPath(string sessionId) =>
        Path.Combine(_basePath, $"{sessionId}.meta.json");
}
