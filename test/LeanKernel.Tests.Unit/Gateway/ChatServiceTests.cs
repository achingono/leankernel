using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LeanKernel.Tests.Unit.Gateway;

public class ChatServiceTests
{
    [Fact]
    public async Task InitializeAsync_filters_internal_continuation_turns_from_history_and_session_title()
    {
        const string sessionId = "session-1";
        const string channelId = "blazor:conversation-1";

        var store = new RecordingSessionStore();
        store.SetHistory(
            sessionId,
            [
                new ConversationTurn
                {
                    Role = "user",
                    Content = "Continue working on the task. Do not repeat completed steps; pick up where you left off.",
                    Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z"),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["auto_continuation"] = "true"
                    }
                },
                new ConversationTurn
                {
                    Role = "user",
                    Content = "Continue working on the task. Do not repeat completed steps; pick up where you left off.",
                    Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:01Z"),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["internal_turn"] = "true",
                        ["internal_reason"] = "auto_continuation_prompt"
                    }
                },
                new ConversationTurn
                {
                    Role = "user",
                    Content = "Please summarize what is left.",
                    Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:02Z")
                },
                new ConversationTurn
                {
                    Role = "assistant",
                    Content = "Here is what is left to do.",
                    Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:03Z")
                }
            ]);

        var service = new ChatService(
            new Mock<IAgentRuntime>(MockBehavior.Strict).Object,
            store,
            NullLogger<ChatService>.Instance);

        await service.InitializeAsync(
            ownerId: "user-1",
            cachedSessions:
            [
                new ChatSessionSummary
                {
                    SessionId = sessionId,
                    ChannelId = channelId,
                    Title = "stale",
                    Preview = "stale",
                    CreatedAt = DateTimeOffset.Parse("2025-05-20T09:59:00Z"),
                    UpdatedAt = DateTimeOffset.Parse("2025-05-20T10:00:03Z"),
                    HasMessages = true,
                }
            ],
            requestedSessionId: sessionId,
            ct: CancellationToken.None);

        service.Messages.Select(message => message.Content)
            .Should()
            .Equal("Please summarize what is left.", "Here is what is left to do.");

        service.Sessions.Should().ContainSingle(session =>
            session.SessionId == sessionId
            && session.Title == "Please summarize what is left."
            && session.Preview == "Here is what is left to do.");
    }

    private sealed class RecordingSessionStore : ISessionStore
    {
        private readonly Dictionary<string, IReadOnlyList<ConversationTurn>> _history = new(StringComparer.Ordinal);

        public void SetHistory(string sessionId, IReadOnlyList<ConversationTurn> turns)
        {
            _history[sessionId] = turns;
        }

        public Task<string> GetOrCreateSessionIdAsync(string channelId, string userId, CancellationToken ct = default)
            => Task.FromResult("session-1");

        public Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, int maxTurns = 50, CancellationToken ct = default)
            => Task.FromResult(_history.TryGetValue(sessionId, out var turns)
                ? turns
                : Array.Empty<ConversationTurn>());

        public Task<bool> SessionBelongsToUserAsync(string sessionId, string userId, CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
