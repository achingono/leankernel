using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Archivist.Sessions;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Archivist;

public class ConversationCompactorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SessionStore _sessionStore;

    public ConversationCompactorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_compact_{Guid.NewGuid():N}");
        _sessionStore = new SessionStore(_tempDir, NullLogger<SessionStore>.Instance);
    }

    [Fact]
    public async Task CompactSessionAsync_FewTurns_NoOp()
    {
        var wiki = Substitute.For<IWikiStore>();
        var extractor = Substitute.For<IWikiFactExtractor>();
        var compactor = new ConversationCompactor(
            _sessionStore,
            wiki,
            extractor,
            new WikiFactMapper(),
            NullLogger<ConversationCompactor>.Instance);

        // Add 10 turns (< 16, no compaction needed)
        for (int i = 0; i < 10; i++)
            await _sessionStore.AppendTurnAsync("s1", MakeTurn(i % 2 == 0 ? "user" : "assistant", $"Msg {i}"), CancellationToken.None);

        await compactor.CompactSessionAsync("s1", CancellationToken.None);

        // Wiki should not receive any facts
        await wiki.DidNotReceive().UpsertAsync(Arg.Any<WikiEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompactSessionAsync_ManyTurns_ExtractsFactsAndCompacts()
    {
        var wiki = Substitute.For<IWikiStore>();
        var extractor = Substitute.For<IWikiFactExtractor>();
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExtractedWikiFact>>([
                new ExtractedWikiFact
                {
                    PrimaryDimension = "who",
                    Subject = "Person",
                    Claim = "Person stated a profile fact",
                    Who = "Person",
                    SourceQuote = "my name is Person"
                }
            ]));
        var compactor = new ConversationCompactor(
            _sessionStore,
            wiki,
            extractor,
            new WikiFactMapper(),
            NullLogger<ConversationCompactor>.Instance);

        // Add 20 turns (pairs of user + assistant)
        for (int i = 0; i < 20; i++)
        {
            var role = i % 2 == 0 ? "user" : "assistant";
            var content = role == "user" ? $"My name is Person{i}" : $"Nice to meet you Person{i}";
            await _sessionStore.AppendTurnAsync("s1", MakeTurn(role, content), CancellationToken.None);
        }

        await compactor.CompactSessionAsync("s1", CancellationToken.None);

        // After compaction, session should have ≤ 15 turns
        var history = await _sessionStore.GetHistoryAsync("s1", CancellationToken.None);
        Assert.True(history.Count <= 15);
    }

    [Fact]
    public async Task CompactSessionAsync_EmptySession_NoOp()
    {
        var wiki = Substitute.For<IWikiStore>();
        var extractor = Substitute.For<IWikiFactExtractor>();
        var compactor = new ConversationCompactor(
            _sessionStore,
            wiki,
            extractor,
            new WikiFactMapper(),
            NullLogger<ConversationCompactor>.Instance);

        await compactor.CompactSessionAsync("empty", CancellationToken.None);
        await wiki.DidNotReceive().UpsertAsync(Arg.Any<WikiEntry>(), Arg.Any<CancellationToken>());
    }

    private static ConversationTurn MakeTurn(string role, string content) => new()
    {
        Role = role,
        Content = content,
        Timestamp = DateTimeOffset.UtcNow
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
