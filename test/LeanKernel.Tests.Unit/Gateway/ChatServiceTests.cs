using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using System.Reflection;

namespace LeanKernel.Tests.Unit.Gateway;

public class ChatServiceTests
{
    [Fact]
    public void Constructor_throws_on_null_agent_runtime()
    {
        var act = () => new ChatService(
            null!,
            Mock.Of<ISessionStore>(),
            NullLogger<ChatService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("agentRuntime");
    }

    [Fact]
    public void Constructor_throws_on_null_session_store()
    {
        var act = () => new ChatService(
            Mock.Of<IAgentRuntime>(),
            null!,
            NullLogger<ChatService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("sessionStore");
    }

    [Fact]
    public void Constructor_throws_on_null_logger()
    {
        var act = () => new ChatService(
            Mock.Of<IAgentRuntime>(),
            Mock.Of<ISessionStore>(),
            null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task InitializeAsync_sets_OwnerId_and_IsInitialized()
    {
        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            new RecordingSessionStore(),
            NullLogger<ChatService>.Instance);

        await service.InitializeAsync("user-1", [], null);

        service.OwnerId.Should().Be("user-1");
        service.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_throws_on_null_ownerId()
    {
        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            new RecordingSessionStore(),
            NullLogger<ChatService>.Instance);

        var act = () => service.InitializeAsync(null!, [], null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnsureReady_throws_when_not_initialized()
    {
        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            new RecordingSessionStore(),
            NullLogger<ChatService>.Instance);

        var act = () => service.CreateNewSessionAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("ChatService must be initialized before use.");
    }

    [Fact]
    public async Task CreateNewSessionAsync_returns_session_id()
    {
        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            new RecordingSessionStore(),
            NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], null);

        var sessionId = await service.CreateNewSessionAsync();

        sessionId.Should().NotBeNullOrWhiteSpace();
        service.CurrentSessionId.Should().Be(sessionId);
    }

    [Fact]
    public async Task CreateNewSessionAsync_fires_SessionsChanged_event()
    {
        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            new RecordingSessionStore(),
            NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], null);

        var eventFired = false;
        service.SessionsChanged += () => eventFired = true;

        await service.CreateNewSessionAsync();

        eventFired.Should().BeTrue();
    }

    [Fact]
    public async Task CreateNewSessionAsync_clears_messages_and_error()
    {
        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            new RecordingSessionStore(),
            NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], null);
        service.Messages.Add(new ChatMessageViewModel
        {
            Id = "old-msg",
            Role = "user",
            Content = "old",
        });

        await service.CreateNewSessionAsync();

        service.Messages.Should().BeEmpty();
        service.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_returns_false_when_content_is_empty()
    {
        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            new RecordingSessionStore(),
            NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], null);

        service.ComposerText = "   ";

        var result = await service.SendAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_creates_new_session_when_CurrentSessionId_is_null()
    {
        var agentMock = new Mock<IAgentRuntime>(MockBehavior.Strict);
        agentMock.Setup(a => a.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("response");

        var store = new RecordingSessionStore();
        store.SetHistory("session-1", [
            new ConversationTurn { Role = "user", Content = "Hello", Timestamp = DateTimeOffset.UtcNow },
            new ConversationTurn { Role = "assistant", Content = "Hi!", Timestamp = DateTimeOffset.UtcNow.AddSeconds(1) },
        ]);

        var service = new ChatService(agentMock.Object, store, NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], null);
        service.ComposerText = "Hello";

        var result = await service.SendAsync();

        result.Should().BeTrue();
        service.CurrentSessionId.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_invokes_agent_runtime_with_user_message()
    {
        var agentMock = new Mock<IAgentRuntime>(MockBehavior.Strict);
        agentMock.Setup(a => a.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("response");

        var store = new RecordingSessionStore();
        store.SetHistory("session-1", [
            new ConversationTurn { Role = "user", Content = "Hello", Timestamp = DateTimeOffset.UtcNow },
            new ConversationTurn { Role = "assistant", Content = "Hi!", Timestamp = DateTimeOffset.UtcNow.AddSeconds(1) },
        ]);

        var service = new ChatService(agentMock.Object, store, NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], "session-1");
        service.ComposerText = "Hello";

        var result = await service.SendAsync();

        result.Should().BeTrue();
        agentMock.Verify(a => a.RunTurnAsync(
            It.Is<LeanKernelMessage>(m => m.Content == "Hello" && m.SenderId == "user-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_includes_ui_surface_metadata()
    {
        var agentMock = new Mock<IAgentRuntime>(MockBehavior.Strict);
        agentMock.Setup(a => a.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("response");

        var store = new RecordingSessionStore();
        store.SetHistory("session-1", [
            new ConversationTurn { Role = "user", Content = "Hello", Timestamp = DateTimeOffset.UtcNow },
            new ConversationTurn { Role = "assistant", Content = "Hi!", Timestamp = DateTimeOffset.UtcNow.AddSeconds(1) },
        ]);

        var service = new ChatService(agentMock.Object, store, NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], "session-1");
        service.ComposerText = "Hello";

        await service.SendAsync();

        agentMock.Verify(a => a.RunTurnAsync(
            It.Is<LeanKernelMessage>(m =>
                m.Metadata != null
                && m.Metadata.ContainsKey("ui_surface")
                && m.Metadata["ui_surface"] == "blazor-chat"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_clears_ComposerText_on_success()
    {
        var agentMock = new Mock<IAgentRuntime>(MockBehavior.Strict);
        agentMock.Setup(a => a.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("response");

        var store = new RecordingSessionStore();
        store.SetHistory("session-1", [
            new ConversationTurn { Role = "user", Content = "Hello", Timestamp = DateTimeOffset.UtcNow },
            new ConversationTurn { Role = "assistant", Content = "Hi!", Timestamp = DateTimeOffset.UtcNow.AddSeconds(1) },
        ]);

        var service = new ChatService(agentMock.Object, store, NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], "session-1");
        service.ComposerText = "Hello";

        await service.SendAsync();

        service.ComposerText.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_loads_messages_after_turn()
    {
        var agentMock = new Mock<IAgentRuntime>(MockBehavior.Strict);
        agentMock.Setup(a => a.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("response");

        var store = new RecordingSessionStore();
        store.SetHistory("session-1", [
            new ConversationTurn { Role = "user", Content = "Hello", Timestamp = DateTimeOffset.UtcNow },
            new ConversationTurn { Role = "assistant", Content = "Hi there!", Timestamp = DateTimeOffset.UtcNow.AddSeconds(1) },
        ]);

        var service = new ChatService(agentMock.Object, store, NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], "session-1");
        service.ComposerText = "Hello";

        await service.SendAsync();

        service.Messages.Select(m => m.Content).Should().Equal("Hello", "Hi there!");
    }

    [Fact]
    public async Task SendAsync_removes_pending_message_on_exception()
    {
        var agentMock = new Mock<IAgentRuntime>(MockBehavior.Strict);
        agentMock.Setup(a => a.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var store = new RecordingSessionStore();
        store.SetHistory("session-1", [
            new ConversationTurn { Role = "user", Content = "Previous", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) },
        ]);

        var service = new ChatService(agentMock.Object, store, NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], "session-1");
        service.ComposerText = "Hello";

        await service.SendAsync();

        service.Messages.Should().NotContain(m => m.Status == ChatMessageStatus.Pending);
    }

    [Fact]
    public async Task SendAsync_sets_ErrorMessage_on_failure()
    {
        var agentMock = new Mock<IAgentRuntime>(MockBehavior.Strict);
        agentMock.Setup(a => a.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var store = new RecordingSessionStore();
        store.SetHistory("session-1", []);

        var service = new ChatService(agentMock.Object, store, NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], "session-1");
        service.ComposerText = "Hello";

        var result = await service.SendAsync();

        result.Should().BeFalse();
        service.ErrorMessage.Should().Be("The turn could not be completed. Please try again.");
    }

    [Fact]
    public async Task SendAsync_toggles_IsLoading_during_execution()
    {
        var tcs = new TaskCompletionSource<string>();
        var agentMock = new Mock<IAgentRuntime>(MockBehavior.Strict);
        agentMock.Setup(a => a.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var store = new RecordingSessionStore();
        store.SetHistory("session-1", []);

        var service = new ChatService(agentMock.Object, store, NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], "session-1");
        service.ComposerText = "Hello";

        var sendTask = service.SendAsync();

        service.IsLoading.Should().BeTrue();
        tcs.TrySetResult("done");
        await sendTask;

        service.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_filters_internal_continuation_turns_from_history()
    {
        var store = new RecordingSessionStore();
        store.SetHistory("session-1", [
            new ConversationTurn
            {
                Role = "user",
                Content = "Continue working...",
                Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z"),
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["auto_continuation"] = "true"
                }
            },
            new ConversationTurn
            {
                Role = "user",
                Content = "Real question",
                Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:01Z"),
            },
            new ConversationTurn
            {
                Role = "assistant",
                Content = "Real answer",
                Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:02Z"),
            },
        ]);

        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            store,
            NullLogger<ChatService>.Instance);

        await service.InitializeAsync(
            "user-1",
            [
                new ChatSessionSummary
                {
                    SessionId = "session-1",
                    ChannelId = "blazor:conv-1",
                    Title = "stale",
                    Preview = "stale",
                    CreatedAt = DateTimeOffset.Parse("2025-05-20T09:59:00Z"),
                    UpdatedAt = DateTimeOffset.Parse("2025-05-20T10:00:02Z"),
                    HasMessages = true,
                }
            ],
            "session-1");

        service.Messages.Select(m => m.Content)
            .Should().Equal("Real question", "Real answer");

        service.Sessions.Should().ContainSingle(s =>
            s.SessionId == "session-1"
            && s.Title == "Real question"
            && s.Preview == "Real answer");
    }

    [Fact]
    public async Task OwnerId_returns_initialized_value()
    {
        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            new RecordingSessionStore(),
            NullLogger<ChatService>.Instance);
        await service.InitializeAsync("test-owner", [], null);

        service.OwnerId.Should().Be("test-owner");
    }

    [Fact]
    public async Task InitializeAsync_clears_ErrorMessage()
    {
        var service = new ChatService(
            Mock.Of<IAgentRuntime>(),
            new RecordingSessionStore(),
            NullLogger<ChatService>.Instance);
        await service.InitializeAsync("user-1", [], "session-1");

        service.ErrorMessage.Should().BeNull();
    }

    private static object? InvokeStatic(string methodName, Type[] paramTypes, params object?[] args)
    {
        var method = typeof(ChatService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static, null, paramTypes, null)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found.");
        return method.Invoke(null, args);
    }

    // ---------- IsInternalContinuationPrompt(IReadOnlyDictionary<string, string>?) ----------

    [Fact]
    public void IsInternalContinuationPrompt_null_metadata_returns_false()
    {
        var result = (bool)InvokeStatic("IsInternalContinuationPrompt", [typeof(IReadOnlyDictionary<string, string>)], new object?[] { null })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInternalContinuationPrompt_empty_metadata_returns_false()
    {
        var metadata = new Dictionary<string, string>();
        var result = (bool)InvokeStatic("IsInternalContinuationPrompt", [typeof(IReadOnlyDictionary<string, string>)], metadata)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInternalContinuationPrompt_internal_turn_with_correct_reason_returns_true()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["internal_turn"] = "true",
            ["internal_reason"] = "auto_continuation_prompt",
        };
        var result = (bool)InvokeStatic("IsInternalContinuationPrompt", [typeof(IReadOnlyDictionary<string, string>)], metadata)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsInternalContinuationPrompt_auto_continuation_true_returns_true()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["auto_continuation"] = "true",
        };
        var result = (bool)InvokeStatic("IsInternalContinuationPrompt", [typeof(IReadOnlyDictionary<string, string>)], metadata)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsInternalContinuationPrompt_auto_continuation_1_returns_true()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["auto_continuation"] = "1",
        };
        var result = (bool)InvokeStatic("IsInternalContinuationPrompt", [typeof(IReadOnlyDictionary<string, string>)], metadata)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsInternalContinuationPrompt_internal_turn_with_wrong_reason_returns_false()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["internal_turn"] = "true",
            ["internal_reason"] = "user_initiated",
        };
        var result = (bool)InvokeStatic("IsInternalContinuationPrompt", [typeof(IReadOnlyDictionary<string, string>)], metadata)!;
        result.Should().BeFalse();
    }

    // ---------- IsInternalContinuationPrompt(string?) ----------

    [Fact]
    public void IsInternalContinuationPrompt_null_json_returns_false()
    {
        var result = (bool)InvokeStatic("IsInternalContinuationPrompt", [typeof(string)], new object?[] { null })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInternalContinuationPrompt_invalid_json_returns_false()
    {
        var result = (bool)InvokeStatic("IsInternalContinuationPrompt", [typeof(string)], "not-json")!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInternalContinuationPrompt_valid_json_with_auto_continuation_returns_true()
    {
        const string json = """{"auto_continuation": "true"}""";
        var result = (bool)InvokeStatic("IsInternalContinuationPrompt", [typeof(string)], json)!;
        result.Should().BeTrue();
    }

    // ---------- BuildPreview(string?) ----------

    [Fact]
    public void BuildPreview_null_returns_start_conversation()
    {
        var result = (string)InvokeStatic("BuildPreview", [typeof(string)], new object?[] { null })!;
        result.Should().Be("Start a conversation");
    }

    [Fact]
    public void BuildPreview_short_content_returns_content()
    {
        var result = (string)InvokeStatic("BuildPreview", [typeof(string)], "Hello, world!")!;
        result.Should().Be("Hello, world!");
    }

    [Fact]
    public void BuildPreview_long_content_truncates()
    {
        var longContent = new string('x', 73);
        var result = (string)InvokeStatic("BuildPreview", [typeof(string)], longContent)!;
        result.Should().Be(new string('x', 69) + "...");
        result.Length.Should().Be(72);
    }

    // ---------- BuildTitle(IReadOnlyList<ConversationTurn>?, string?) ----------

    [Fact]
    public void BuildTitle_null_turns_and_null_fallback_returns_new_session()
    {
        var result = (string)InvokeStatic("BuildTitle", [typeof(IReadOnlyList<ConversationTurn>), typeof(string)], null, null)!;
        result.Should().Be("New session");
    }

    [Fact]
    public void BuildTitle_with_user_message_returns_first_user_message_truncated()
    {
        var longUserContent = new string('a', 40);
        var turns = new List<ConversationTurn>
        {
            new() { Role = "user", Content = longUserContent, Timestamp = DateTimeOffset.UtcNow },
        };
        var result = (string)InvokeStatic("BuildTitle", [typeof(IReadOnlyList<ConversationTurn>), typeof(string)], turns, null)!;
        result.Should().Be(new string('a', 33) + "...");
        result.Length.Should().Be(36);
    }

    [Fact]
    public void BuildTitle_with_only_internal_continuation_returns_fallback()
    {
        var turns = new List<ConversationTurn>
        {
            new()
            {
                Role = "user",
                Content = "internal",
                Timestamp = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["auto_continuation"] = "true",
                },
            },
        };
        var result = (string)InvokeStatic("BuildTitle", [typeof(IReadOnlyList<ConversationTurn>), typeof(string)], turns, "Fallback title")!;
        result.Should().Be("Fallback title");
    }
}

/// <summary>
/// Test double for <see cref="ISessionStore"/> that provides pre-canned history and auto-accepts session creation.
/// </summary>
public class RecordingSessionStore : ISessionStore
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
