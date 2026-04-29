using Microsoft.Extensions.AI;
using LeanKernel.Core.Models;
using LeanKernel.Thinker;
using Xunit;

namespace LeanKernel.Tests.Unit.Thinker;

public class SessionExtensionsTests
{
    [Theory]
    [InlineData("user", "Hello")]
    [InlineData("assistant", "Hi there")]
    [InlineData("system", "You are helpful")]
    public void ToChatMessage_MapsRoleAndContent(string role, string content)
    {
        var turn = new ConversationTurn
        {
            Role = role,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow
        };

        var msg = turn.ToChatMessage();

        var expectedRole = role switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };
        Assert.Equal(expectedRole, msg.Role);
        Assert.Equal(content, msg.Text);
    }

    [Fact]
    public void ToConversationTurn_MapsFromChatMessage()
    {
        var msg = new ChatMessage(ChatRole.Assistant, "I can help");

        var turn = msg.ToConversationTurn();

        Assert.Equal("assistant", turn.Role);
        Assert.Equal("I can help", turn.Content);
        Assert.True(turn.Timestamp <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ToChatMessages_PreservesOrderAndContent()
    {
        var turns = new List<ConversationTurn>
        {
            new() { Role = "user", Content = "First", Timestamp = DateTimeOffset.UtcNow },
            new() { Role = "assistant", Content = "Second", Timestamp = DateTimeOffset.UtcNow },
            new() { Role = "user", Content = "Third", Timestamp = DateTimeOffset.UtcNow }
        };

        var messages = turns.ToChatMessages();

        Assert.Equal(3, messages.Count);
        Assert.Equal(ChatRole.User, messages[0].Role);
        Assert.Equal("First", messages[0].Text);
        Assert.Equal(ChatRole.Assistant, messages[1].Role);
        Assert.Equal("Second", messages[1].Text);
    }

    [Fact]
    public void ToConversationTurns_FiltersOutSystemMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt"),
            new(ChatRole.User, "User input"),
            new(ChatRole.Assistant, "Response")
        };

        var turns = messages.ToConversationTurns();

        Assert.Equal(2, turns.Count);
        Assert.Equal("user", turns[0].Role);
        Assert.Equal("assistant", turns[1].Role);
    }

    [Fact]
    public void RoundTrip_ConversationTurn_Preserves()
    {
        var original = new ConversationTurn
        {
            Role = "user",
            Content = "Round trip test",
            Timestamp = DateTimeOffset.UtcNow
        };

        var msg = original.ToChatMessage();
        var roundTripped = msg.ToConversationTurn();

        Assert.Equal(original.Role, roundTripped.Role);
        Assert.Equal(original.Content, roundTripped.Content);
    }
}
