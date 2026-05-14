using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Services;
using NSubstitute;

namespace LeanKernel.Tests.Unit.Thinker;

public sealed class LlmFactExtractionStepTests
{
    [Fact]
    public async Task ProcessAsync_IngestsMappedFacts()
    {
        var extractor = Substitute.For<IWikiFactExtractor>();
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExtractedWikiFact>>([
                new ExtractedWikiFact
                {
                    Subject = "Ada",
                    PrimaryDimension = "who",
                    Claim = "Ada prefers concise answers.",
                    Who = "Ada",
                    SourceQuote = "I prefer concise answers."
                }
            ]));
        var wiki = Substitute.For<IWikiStore>();
        var step = new LlmFactExtractionStep(extractor, new WikiFactMapper(), wiki);

        var result = await step.ProcessAsync(CreateTurnEvent(), CancellationToken.None);

        Assert.True(result.Success);
        await wiki.Received(1).IngestFactsAsync(
            Arg.Is<IEnumerable<WikiEntry>>(entries => entries.Any(e => e.Id == "who-ada")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_NoFacts_DoesNotIngest()
    {
        var extractor = Substitute.For<IWikiFactExtractor>();
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExtractedWikiFact>>([]));
        var wiki = Substitute.For<IWikiStore>();
        var step = new LlmFactExtractionStep(extractor, new WikiFactMapper(), wiki);

        var result = await step.ProcessAsync(CreateTurnEvent(), CancellationToken.None);

        Assert.True(result.Success);
        await wiki.DidNotReceive().IngestFactsAsync(Arg.Any<IEnumerable<WikiEntry>>(), Arg.Any<CancellationToken>());
    }

    private static TurnEvent CreateTurnEvent() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionId = "session-1",
        UserMessage = new LeanKernelMessage
        {
            Id = "msg-1",
            ChannelId = "test",
            SenderId = "user",
            Content = "I prefer concise answers.",
            Timestamp = DateTimeOffset.UtcNow
        },
        AssistantResponse = "Got it.",
        SourceId = "conversation:test"
    };
}
