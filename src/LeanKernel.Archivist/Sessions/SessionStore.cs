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

    public SessionStore(string sessionsPath, ILogger<SessionStore> logger)
    {
        _basePath = sessionsPath;
        _logger = logger;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<List<ConversationTurn>> GetHistoryAsync(string sessionId, CancellationToken ct)
    {
        var path = GetSessionPath(sessionId);
        if (!File.Exists(path)) return [];

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ConversationTurn>>(stream, JsonOptions, ct) ?? [];
    }

    public async Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct)
    {
        var history = await GetHistoryAsync(sessionId, ct);
        history.Add(turn);

        var path = GetSessionPath(sessionId);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, history, JsonOptions, ct);
    }

    public Task<string> GetOrCreateSessionIdAsync(string channelId, string senderId, CancellationToken ct)
    {
        // Deterministic session ID from channel + sender
        var sessionId = $"{channelId}_{senderId}";
        return Task.FromResult(sessionId);
    }

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

    public Task<IReadOnlyList<string>> ListSessionsAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_basePath))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var sessions = Directory.GetFiles(_basePath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderDescending()
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(sessions);
    }

    private string GetSessionPath(string sessionId) =>
        Path.Combine(_basePath, $"{sessionId}.json");
}
