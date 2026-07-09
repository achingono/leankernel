using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using LeanKernel.Persistence.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LeanKernel.Tests.Unit.Persistence;

public class ResilientSessionStoreTests
{
    private const string ChannelId = "test-channel";
    private const string UserId = "test-user";
    private const string SessionId = "test-session";

    [Fact]
    public async Task GetOrCreateSessionIdAsync_returns_session_id_when_inner_succeeds()
    {
        var factory = CreateFactory();
        var innerStore = CreateInnerStore(factory);
        var sut = CreateSut(innerStore);

        var result = await sut.GetOrCreateSessionIdAsync(ChannelId, UserId);

        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetOrCreateSessionIdAsync_records_healthy_when_inner_succeeds()
    {
        var factory = CreateFactory();
        var innerStore = CreateInnerStore(factory);
        var tracker = CreateHealthTracker();
        var sut = CreateSut(innerStore, healthTracker: tracker.Object);

        await sut.GetOrCreateSessionIdAsync(ChannelId, UserId);

        tracker.Verify(t => t.RecordProbeResult(
            ProviderNames.Database,
            It.Is<ProviderProbeResult>(r => r.IsHealthy)), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateSessionIdAsync_falls_back_to_buffer_when_inner_throws()
    {
        var innerStore = CreateFailingInnerStore();
        var buffer = new DegradedSessionBuffer();
        var sut = CreateSut(innerStore, buffer);

        var result = await sut.GetOrCreateSessionIdAsync(ChannelId, UserId);

        result.Should().NotBeNullOrWhiteSpace();
        buffer.SessionBelongsToUser(result, UserId).Should().BeTrue();
    }

    [Fact]
    public async Task GetOrCreateSessionIdAsync_records_unhealthy_when_inner_throws()
    {
        var innerStore = CreateFailingInnerStore();
        var tracker = CreateHealthTracker();
        var sut = CreateSut(innerStore, healthTracker: tracker.Object);

        await sut.GetOrCreateSessionIdAsync(ChannelId, UserId);

        tracker.Verify(t => t.RecordProbeResult(
            ProviderNames.Database,
            It.Is<ProviderProbeResult>(r => !r.IsHealthy)), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateSessionIdAsync_rethrows_when_cancellation_requested()
    {
        var factory = CreateFactory();
        var innerStore = CreateInnerStore(factory);
        var sut = CreateSut(innerStore);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sut.GetOrCreateSessionIdAsync(ChannelId, UserId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AppendTurnAsync_completes_when_inner_succeeds()
    {
        var factory = CreateFactory();
        await SeedSessionAsync(factory, SessionId);
        var innerStore = CreateInnerStore(factory);
        var sut = CreateSut(innerStore);
        var turn = CreateTurn("turn-1");

        var act = () => sut.AppendTurnAsync(SessionId, turn);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AppendTurnAsync_records_healthy_when_inner_succeeds()
    {
        var factory = CreateFactory();
        await SeedSessionAsync(factory, SessionId);
        var innerStore = CreateInnerStore(factory);
        var tracker = CreateHealthTracker();
        var sut = CreateSut(innerStore, healthTracker: tracker.Object);

        await sut.AppendTurnAsync(SessionId, CreateTurn("turn-1"));

        tracker.Verify(t => t.RecordProbeResult(
            ProviderNames.Database,
            It.Is<ProviderProbeResult>(r => r.IsHealthy)), Times.Once);
    }

    [Fact]
    public async Task AppendTurnAsync_falls_back_to_buffer_when_inner_throws()
    {
        var innerStore = CreateFailingInnerStore();
        var buffer = new DegradedSessionBuffer();
        var sut = CreateSut(innerStore, buffer);
        var turn = CreateTurn("turn-1");

        var act = () => sut.AppendTurnAsync(SessionId, turn);

        await act.Should().NotThrowAsync();
        var history = buffer.GetHistory(SessionId, 10);
        history.Should().ContainSingle(t => t.TurnId == "turn-1");
    }

    [Fact]
    public async Task AppendTurnAsync_records_unhealthy_when_inner_throws()
    {
        var innerStore = CreateFailingInnerStore();
        var tracker = CreateHealthTracker();
        var sut = CreateSut(innerStore, healthTracker: tracker.Object);

        await sut.AppendTurnAsync(SessionId, CreateTurn("turn-1"));

        tracker.Verify(t => t.RecordProbeResult(
            ProviderNames.Database,
            It.Is<ProviderProbeResult>(r => !r.IsHealthy)), Times.Once);
    }

    [Fact]
    public async Task AppendTurnAsync_rethrows_when_cancellation_requested()
    {
        var factory = CreateFactory();
        await SeedSessionAsync(factory, SessionId);
        var innerStore = CreateInnerStore(factory);
        var sut = CreateSut(innerStore);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sut.AppendTurnAsync(SessionId, CreateTurn("turn-1"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SessionBelongsToUserAsync_returns_true_when_inner_succeeds()
    {
        var factory = CreateFactory();
        await SeedSessionAsync(factory, SessionId, UserId);
        var innerStore = CreateInnerStore(factory);
        var sut = CreateSut(innerStore);

        var result = await sut.SessionBelongsToUserAsync(SessionId, UserId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SessionBelongsToUserAsync_records_healthy_when_inner_succeeds()
    {
        var factory = CreateFactory();
        await SeedSessionAsync(factory, SessionId, UserId);
        var innerStore = CreateInnerStore(factory);
        var tracker = CreateHealthTracker();
        var sut = CreateSut(innerStore, healthTracker: tracker.Object);

        await sut.SessionBelongsToUserAsync(SessionId, UserId);

        tracker.Verify(t => t.RecordProbeResult(
            ProviderNames.Database,
            It.Is<ProviderProbeResult>(r => r.IsHealthy)), Times.Once);
    }

    [Fact]
    public async Task SessionBelongsToUserAsync_falls_back_to_buffer_when_inner_throws()
    {
        var innerStore = CreateFailingInnerStore();
        var buffer = new DegradedSessionBuffer();
        buffer.GetOrCreateSessionId(ChannelId, UserId);
        var sut = CreateSut(innerStore, buffer);

        var result = await sut.SessionBelongsToUserAsync(SessionId, UserId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SessionBelongsToUserAsync_rethrows_when_cancellation_requested()
    {
        var factory = CreateFactory();
        await SeedSessionAsync(factory, SessionId, UserId);
        var innerStore = CreateInnerStore(factory);
        var sut = CreateSut(innerStore);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sut.SessionBelongsToUserAsync(SessionId, UserId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetHistoryAsync_returns_turns_when_inner_succeeds()
    {
        var factory = CreateFactory();
        await SeedSessionAsync(factory, SessionId);
        var turn = CreateTurn("turn-1");
        await SeedTurnAsync(factory, SessionId, turn);
        var innerStore = CreateInnerStore(factory);
        var sut = CreateSut(innerStore);

        var result = await sut.GetHistoryAsync(SessionId);

        result.Should().ContainSingle();
        result[0].TurnId.Should().Be("turn-1");
    }

    [Fact]
    public async Task GetHistoryAsync_returns_turns_from_buffer_when_inner_throws()
    {
        var innerStore = CreateFailingInnerStore();
        var buffer = new DegradedSessionBuffer();
        var expectedTurn = CreateTurn("fallback-turn");
        buffer.AppendTurn(SessionId, expectedTurn);
        var sut = CreateSut(innerStore, buffer);

        var result = await sut.GetHistoryAsync(SessionId);

        result.Should().ContainSingle();
        result[0].TurnId.Should().Be("fallback-turn");
    }

    [Fact]
    public async Task GetHistoryAsync_records_unhealthy_when_inner_throws()
    {
        var innerStore = CreateFailingInnerStore();
        var tracker = CreateHealthTracker();
        var sut = CreateSut(innerStore, healthTracker: tracker.Object);

        await sut.GetHistoryAsync(SessionId);

        tracker.Verify(t => t.RecordProbeResult(
            ProviderNames.Database,
            It.Is<ProviderProbeResult>(r => !r.IsHealthy)), Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_rethrows_when_cancellation_requested()
    {
        var factory = CreateFactory();
        var innerStore = CreateInnerStore(factory);
        var sut = CreateSut(innerStore);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sut.GetHistoryAsync(SessionId, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ResilientSessionStore CreateSut(
        PostgresSessionStore? innerStore = null,
        DegradedSessionBuffer? buffer = null,
        IProviderHealthTracker? healthTracker = null)
    {
        var factory = CreateFactory();
        return new ResilientSessionStore(
            innerStore ?? CreateInnerStore(factory),
            buffer ?? new DegradedSessionBuffer(),
            NullLogger<ResilientSessionStore>.Instance,
            healthTracker);
    }

    private static PostgresSessionStore CreateInnerStore(TestDbContextFactory factory)
        => new(factory, NullLogger<PostgresSessionStore>.Instance);

    private static PostgresSessionStore CreateFailingInnerStore()
        => new(new ThrowingDbFactory(), NullLogger<PostgresSessionStore>.Instance);

    private static TestDbContextFactory CreateFactory()
        => new(new DbContextOptionsBuilder<LeanKernelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Mock<IProviderHealthTracker> CreateHealthTracker()
        => new(MockBehavior.Loose);

    private static ConversationTurn CreateTurn(string turnId)
        => new()
        {
            TurnId = turnId,
            Role = "user",
            Content = $"Turn {turnId}",
            Timestamp = DateTimeOffset.UtcNow,
        };

    private static async Task SeedSessionAsync(TestDbContextFactory factory, string sessionId, string? userId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.Sessions.Add(new LeanKernel.Persistence.Entities.SessionEntity
        {
            Id = sessionId,
            ChannelId = ChannelId,
            UserId = userId ?? UserId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedTurnAsync(TestDbContextFactory factory, string sessionId, ConversationTurn turn)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.Turns.Add(new LeanKernel.Persistence.Entities.TurnEntity
        {
            Id = turn.TurnId ?? Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            Role = turn.Role,
            Content = turn.Content,
            Timestamp = turn.Timestamp,
        });
        await db.SaveChangesAsync();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<LeanKernelDbContext>
    {
        private readonly DbContextOptions<LeanKernelDbContext> _options;

        public TestDbContextFactory(DbContextOptions<LeanKernelDbContext> options) => _options = options;

        public LeanKernelDbContext CreateDbContext() => new(_options);

        public Task<LeanKernelDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private sealed class ThrowingDbFactory : IDbContextFactory<LeanKernelDbContext>
    {
        public LeanKernelDbContext CreateDbContext() => throw new InvalidOperationException("Simulated database failure.");

        public Task<LeanKernelDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated database failure.");
    }
}
