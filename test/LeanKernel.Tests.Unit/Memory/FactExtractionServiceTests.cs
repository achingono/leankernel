using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

/// <summary>
/// Covers fact extraction parsing and rendering helpers.
/// </summary>
public class FactExtractionServiceTests
{
    /// <summary>
    /// Verifies fallback fact parsing handles the supported response shapes.
    /// </summary>
    [Theory]
    [InlineData("[\"Fact A\", \"Fact B\"]", 2)]
    [InlineData("- Fact A\n- Fact B", 2)]
    [InlineData("Fact A\nFact B", 2)]
    [InlineData("[]", 0)]
    public void ParseFacts_HandlesFallbackShapes(string input, int count)
    {
        var facts = FactExtractionService.ParseFacts(input);
        facts.Should().HaveCount(count);
    }

    /// <summary>
    /// Verifies transcript generation includes the current exchange and history.
    /// </summary>
    [Fact]
    public void TranscriptBuilder_IncludesUserHistoryAndAssistant()
    {
        var history = new[]
        {
            new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "u1"),
            new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "a1")
        };

        var transcript = FactExtractionService.BuildConversationTranscript("hello", "answer", history);
        transcript.Should().Contain("User message:");
        transcript.Should().Contain("Recent history:");
        transcript.Should().Contain("Assistant response:");
    }

    /// <summary>
    /// Verifies extracted facts are parsed from the chat client response.
    /// </summary>
    [Fact]
    public async Task ExtractFactsAsync_UsesChatClientAndParsesArray()
    {
        var chatClient = new StaticChatClient("[\"Fact one\",\"Fact two\"]");
        var service = new FactExtractionService(
            chatClient,
            Options.Create(new FactExtractionSettings()),
            new MemoryPageRenderer());

        var facts = await service.ExtractFactsAsync("u", "a", [], CancellationToken.None);

        facts.Should().ContainInOrder("Fact one", "Fact two");
    }

    /// <summary>
    /// Verifies seed pages contain the required learned fact metadata.
    /// </summary>
    [Fact]
    public void RenderSeedPage_ContainsRequiredMetadata()
    {
        var service = new FactExtractionService(
            new StaticChatClient("[]"),
            Options.Create(new FactExtractionSettings()),
            new MemoryPageRenderer());

        var markdown = service.RenderSeedPage("Budget approved", "sess-1", "turn-2", DateTimeOffset.Parse("2026-07-10T12:00:00Z"));

        markdown.Should().Contain("# Learned Fact");
        markdown.Should().Contain("- Session: sess-1");
        markdown.Should().Contain("- Turn: turn-2");
        markdown.Should().Contain("- RecordedAt: 2026-07-10T12:00:00.0000000+00:00");
    }

    /// <summary>
    /// Returns a fixed chat completion for fact extraction tests.
    /// </summary>
    /// <param name="text">The response text to return.</param>
    private sealed class StaticChatClient(string text) : IChatClient
    {
        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
