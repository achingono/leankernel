using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context;
using LeanKernel.Context.History;
using LeanKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Context.History;

public class HistoryShaperTests
{
    [Fact]
    public async Task ShapeAsync_compacts_prunes_to_budget_and_persists_markers()
    {
        var compactor = new Mock<IConversationCompactor>(MockBehavior.Strict);
        compactor
            .Setup(service => service.SummarizeAsync(It.Is<IReadOnlyList<ConversationTurn>>(turns => turns.Count == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync("summarized");
        compactor
            .Setup(service => service.CompactAsync(It.Is<IReadOnlyList<ConversationTurn>>(turns => turns.Count == 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync("compact");

        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<LeanKernelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var options = Options.Create(new HistoryConfig
        {
            RecentTurnsVerbatim = 2,
            CompactedTurnsMax = 2,
            SummarizedTurnsMax = 1,
            EnableCompaction = true,
            EnableSummarization = true,
            PersistCompactionMarkers = true,
            CompactionModel = "gpt-4o-mini"
        });
        var shaper = new HistoryShaper(
            new HistoryCompactionStrategy(new SimpleTokenEstimator(), options, NullLogger<HistoryCompactionStrategy>.Instance),
            compactor.Object,
            new SimpleTokenEstimator(),
            options,
            NullLogger<HistoryShaper>.Instance,
            factory);

        var result = await shaper.ShapeAsync("session-1", CreateTurns(), budgetTokens: 4);

        result.History.Select(turn => turn.Content).Should().Equal("compact", "1234", "1234");
        result.History[0].IsCompacted.Should().BeTrue();
        result.History[0].CompactionSourceId.Should().Be("t3..t4");
        result.Diagnostics.VerbatimTurns.Should().Be(2);
        result.Diagnostics.CompactedTurns.Should().Be(2);
        result.Diagnostics.SummarizedTurns.Should().Be(0);
        result.Diagnostics.DroppedTurns.Should().Be(2);
        result.Diagnostics.TotalTokensAfter.Should().Be(4);
        result.Diagnostics.Markers.Should().HaveCount(2);

        await using var db = await factory.CreateDbContextAsync();
        db.CompactionMarkers.Should().HaveCount(2);
        db.CompactionMarkers.Select(marker => marker.MarkerType).Should().Equal("summarized", "compacted");
        db.CompactionMarkers.Select(marker => marker.CompactedContent).Should().Equal("summarized", "compact");

        compactor.VerifyAll();
    }

    private static IReadOnlyList<ConversationTurn> CreateTurns()
        => Enumerable.Range(1, 6)
            .Select(index => new ConversationTurn
            {
                TurnId = $"t{index}",
                Role = index % 2 == 0 ? "assistant" : "user",
                Content = "1234",
                Timestamp = DateTimeOffset.Parse($"2025-05-20T10:0{index - 1}:00Z")
            })
            .ToList();

    private sealed class TestDbContextFactory : IDbContextFactory<LeanKernelDbContext>
    {
        private readonly DbContextOptions<LeanKernelDbContext> _options;

        public TestDbContextFactory(DbContextOptions<LeanKernelDbContext> options)
        {
            _options = options;
        }

        public LeanKernelDbContext CreateDbContext() => new(_options);

        public Task<LeanKernelDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
