using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence.Resilience;

namespace LeanKernel.Tests.Unit.Persistence;

public class DegradedSessionBufferTests
{
    [Fact]
    public void GetOrCreateSessionId_returns_same_id_for_same_channel_and_user()
    {
        var buffer = new DegradedSessionBuffer();

        var id1 = buffer.GetOrCreateSessionId("channel-a", "user-1");
        var id2 = buffer.GetOrCreateSessionId("channel-a", "user-1");

        id1.Should().Be(id2);
    }

    [Fact]
    public void GetOrCreateSessionId_returns_different_ids_for_different_users()
    {
        var buffer = new DegradedSessionBuffer();

        var id1 = buffer.GetOrCreateSessionId("channel-a", "user-1");
        var id2 = buffer.GetOrCreateSessionId("channel-a", "user-2");

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void GetOrCreateSessionId_throws_on_empty_channel_or_user()
    {
        var buffer = new DegradedSessionBuffer();
        Assert.Throws<ArgumentNullException>(() => buffer.GetOrCreateSessionId(null!, "user"));
        Assert.Throws<ArgumentNullException>(() => buffer.GetOrCreateSessionId("channel", null!));
    }

    [Fact]
    public void SessionBelongsToUser_returns_true_for_matching_owner()
    {
        var buffer = new DegradedSessionBuffer();
        var sessionId = buffer.GetOrCreateSessionId("channel-a", "user-1");

        buffer.SessionBelongsToUser(sessionId, "user-1").Should().BeTrue();
    }

    [Fact]
    public void SessionBelongsToUser_returns_false_for_non_matching_owner()
    {
        var buffer = new DegradedSessionBuffer();
        var sessionId = buffer.GetOrCreateSessionId("channel-a", "user-1");

        buffer.SessionBelongsToUser(sessionId, "user-2").Should().BeFalse();
    }

    [Fact]
    public void SessionBelongsToUser_returns_false_for_unknown_session()
    {
        var buffer = new DegradedSessionBuffer();

        buffer.SessionBelongsToUser("unknown-session", "user-1").Should().BeFalse();
    }

    [Fact]
    public void SessionBelongsToUser_is_case_insensitive()
    {
        var buffer = new DegradedSessionBuffer();
        var sessionId = buffer.GetOrCreateSessionId("channel-a", "User-1");

        buffer.SessionBelongsToUser(sessionId, "user-1").Should().BeTrue();
    }

    [Fact]
    public void SessionBelongsToUser_throws_on_empty_session_or_user()
    {
        var buffer = new DegradedSessionBuffer();
        Assert.Throws<ArgumentException>(() => buffer.SessionBelongsToUser("", "user"));
        Assert.Throws<ArgumentException>(() => buffer.SessionBelongsToUser("session", ""));
    }

    [Fact]
    public void AppendTurn_and_GetHistory_returns_stored_turns()
    {
        var buffer = new DegradedSessionBuffer();
        var sessionId = buffer.GetOrCreateSessionId("channel-a", "user-1");
        var turn = new ConversationTurn { Role = "user", Content = "hello", TurnId = "turn-1" };

        buffer.AppendTurn(sessionId, turn);
        var history = buffer.GetHistory(sessionId, 10);

        history.Should().ContainSingle();
        history[0].TurnId.Should().Be("turn-1");
    }

    [Fact]
    public void GetHistory_returns_empty_for_unknown_session()
    {
        var buffer = new DegradedSessionBuffer();

        var history = buffer.GetHistory("unknown-session", 10);

        history.Should().BeEmpty();
    }

    [Fact]
    public void GetHistory_respects_maxTurns_limit()
    {
        var buffer = new DegradedSessionBuffer();
        var sessionId = buffer.GetOrCreateSessionId("channel-a", "user-1");

        buffer.AppendTurn(sessionId, new ConversationTurn { Role = "user", Content = "test", TurnId = "turn-1" });
        buffer.AppendTurn(sessionId, new ConversationTurn { Role = "user", Content = "test", TurnId = "turn-2" });
        buffer.AppendTurn(sessionId, new ConversationTurn { Role = "user", Content = "test", TurnId = "turn-3" });

        var history = buffer.GetHistory(sessionId, 2);

        history.Should().HaveCount(2);
        history[0].TurnId.Should().Be("turn-2");
        history[1].TurnId.Should().Be("turn-3");
    }

    [Fact]
    public void GetHistory_returns_empty_when_maxTurns_is_zero_or_negative()
    {
        var buffer = new DegradedSessionBuffer();
        var sessionId = buffer.GetOrCreateSessionId("channel-a", "user-1");
        buffer.AppendTurn(sessionId, new ConversationTurn { Role = "user", Content = "test", TurnId = "turn-1" });

        buffer.GetHistory(sessionId, 0).Should().BeEmpty();
        buffer.GetHistory(sessionId, -1).Should().BeEmpty();
    }

    [Fact]
    public void AppendTurn_throws_on_null_turn()
    {
        var buffer = new DegradedSessionBuffer();
        Assert.Throws<ArgumentNullException>(() => buffer.AppendTurn("session-1", null!));
    }

    [Fact]
    public void GetHistory_throws_on_empty_session()
    {
        var buffer = new DegradedSessionBuffer();
        Assert.Throws<ArgumentException>(() => buffer.GetHistory("", 10));
    }
}
