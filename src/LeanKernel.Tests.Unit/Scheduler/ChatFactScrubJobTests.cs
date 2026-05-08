using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Scheduler.Jobs;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public class ChatFactScrubJobTests
{
    [Fact]
    public async Task ExecuteAsync_ProcessesNewTurns_AndWritesCheckpoints()
    {
        var sessions = Substitute.For<ISessionStore>();
        var wiki = Substitute.For<IWikiStore>();
        var job = new ChatFactScrubJob(sessions, wiki, NullLogger<ChatFactScrubJob>.Instance);

        sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["s1"]));

        sessions.GetMetadataAsync("__system_jobs", "chat-fact-scrub:last-run-utc", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        sessions.GetMetadataAsync("s1", "chat-fact-scrub:last-processed-utc", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        var now = DateTimeOffset.UtcNow;
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>
            {
                new() { Role = "user", Content = "my name is Ada Lovelace", Timestamp = now.AddMinutes(-10) },
                new() { Role = "assistant", Content = "Noted.", Timestamp = now.AddMinutes(-9) }
            }));

        await job.ExecuteAsync(CancellationToken.None);

        await wiki.Received(1).IngestFactsAsync(
            Arg.Is<IEnumerable<WikiEntry>>(entries => entries.Any(e => e.Id == "who-user-profile")),
            Arg.Any<CancellationToken>());

        await sessions.Received(1).SetMetadataAsync(
            "s1",
            "chat-fact-scrub:last-processed-utc",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        await sessions.Received(1).SetMetadataAsync(
            "__system_jobs",
            "chat-fact-scrub:last-run-utc",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsAlreadyProcessedTurns()
    {
        var sessions = Substitute.For<ISessionStore>();
        var wiki = Substitute.For<IWikiStore>();
        var job = new ChatFactScrubJob(sessions, wiki, NullLogger<ChatFactScrubJob>.Instance);

        sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["s1"]));

        var checkpoint = DateTimeOffset.UtcNow.AddMinutes(-5);
        sessions.GetMetadataAsync("__system_jobs", "chat-fact-scrub:last-run-utc", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(checkpoint.ToString("O")));
        sessions.GetMetadataAsync("s1", "chat-fact-scrub:last-processed-utc", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(checkpoint.ToString("O")));

        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>
            {
                new() { Role = "user", Content = "my name is Ada Lovelace", Timestamp = checkpoint.AddMinutes(-10) },
                new() { Role = "assistant", Content = "Noted.", Timestamp = checkpoint.AddMinutes(-9) }
            }));

        await job.ExecuteAsync(CancellationToken.None);

        await wiki.DidNotReceive().IngestFactsAsync(Arg.Any<IEnumerable<WikiEntry>>(), Arg.Any<CancellationToken>());
    }
}