using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using LeanKernel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Persistence;

public class PostgresSessionStoreTests
{
    [Fact]
    public async Task GetOrCreateSessionIdAsync_creates_a_new_session_when_one_does_not_exist()
    {
        var factory = CreateFactory();
        var store = CreateStore(factory);

        var sessionId = await store.GetOrCreateSessionIdAsync("channel-1", "user-1");

        sessionId.Should().NotBeNullOrWhiteSpace();
        await using var db = await factory.CreateDbContextAsync();
        db.Sessions.Should().ContainSingle();
        db.Sessions.Single().Id.Should().Be(sessionId);
    }

    [Fact]
    public async Task GetOrCreateSessionIdAsync_returns_the_existing_session_and_updates_it()
    {
        var factory = CreateFactory();
        var existing = new SessionEntity
        {
            Id = "session-1",
            ChannelId = "channel-1",
            UserId = "user-1",
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        await SeedAsync(factory, db => db.Sessions.Add(existing));
        var store = CreateStore(factory);

        var sessionId = await store.GetOrCreateSessionIdAsync("channel-1", "user-1");

        sessionId.Should().Be("session-1");
        await using var db = await factory.CreateDbContextAsync();
        db.Sessions.Single().UpdatedAt.Should().BeAfter(existing.UpdatedAt);
    }

    [Fact]
    public async Task AppendTurnAsync_persists_turns_and_updates_the_session_timestamp()
    {
        var factory = CreateFactory();
        var session = new SessionEntity
        {
            Id = "session-1",
            ChannelId = "channel-1",
            UserId = "user-1",
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        await SeedAsync(factory, db => db.Sessions.Add(session));
        var store = CreateStore(factory);

        await store.AppendTurnAsync("session-1", new ConversationTurn
        {
            TurnId = "turn-1",
            Role = "user",
            Content = "Hello",
            Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z"),
            Metadata = new Dictionary<string, string> { ["auto_continuation"] = "true" }
        });

        await using var db = await factory.CreateDbContextAsync();
        db.Turns.Should().ContainSingle();
        db.Turns.Single().Id.Should().Be("turn-1");
        db.Turns.Single().Content.Should().Be("Hello");
        db.Turns.Single().Metadata.Should().Contain("auto_continuation");
        db.Sessions.Single().UpdatedAt.Should().BeAfter(session.UpdatedAt);
    }

    [Fact]
    public async Task AppendTurnAsync_throws_when_the_session_is_missing()
    {
        var store = CreateStore(CreateFactory());

        var act = () => store.AppendTurnAsync("missing", new ConversationTurn { Role = "user", Content = "Hello" });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Session 'missing' was not found.*");
    }

    [Fact]
    public async Task GetHistoryAsync_returns_the_newest_turns_in_chronological_order()
    {
        var factory = CreateFactory();
        await SeedAsync(factory, db =>
        {
            db.Sessions.Add(new SessionEntity { Id = "session-1", ChannelId = "channel-1", UserId = "user-1" });
            db.Turns.AddRange(
                new TurnEntity { Id = "t1", SessionId = "session-1", Role = "user", Content = "first", Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z") },
                new TurnEntity { Id = "t2", SessionId = "session-1", Role = "assistant", Content = "second", Timestamp = DateTimeOffset.Parse("2025-05-20T10:01:00Z") },
                new TurnEntity { Id = "t3", SessionId = "session-1", Role = "user", Content = "third", Timestamp = DateTimeOffset.Parse("2025-05-20T10:02:00Z") });
        });
        var store = CreateStore(factory);

        var history = await store.GetHistoryAsync("session-1", 2);

        history.Select(turn => turn.Content).Should().Equal("second", "third");
        history.Select(turn => turn.TurnId).Should().Equal("t2", "t3");
    }

    [Fact]
    public async Task GetHistoryAsync_ignores_invalid_metadata_json()
    {
        var factory = CreateFactory();
        await SeedAsync(factory, db =>
        {
            db.Sessions.Add(new SessionEntity { Id = "session-1", ChannelId = "channel-1", UserId = "user-1" });
            db.Turns.Add(new TurnEntity
            {
                Id = "t1",
                SessionId = "session-1",
                Role = "assistant",
                Content = "hello",
                Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z"),
                Metadata = "{not-json}"
            });
        });
        var store = CreateStore(factory);

        var history = await store.GetHistoryAsync("session-1", 10);

        history.Should().ContainSingle();
        history[0].Metadata.Should().BeNull();
    }

    [Fact]
    public async Task GetHistoryAsync_returns_empty_when_max_turns_is_not_positive()
    {
        var store = CreateStore(CreateFactory());

        var history = await store.GetHistoryAsync("session-1", 0);

        history.Should().BeEmpty();
    }

    [Fact]
    public void AddLeanKernelPersistence_registers_the_db_factory_and_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLeanKernelPersistence(new DatabaseConfig
        {
            ConnectionString = "Host=localhost;Database=leankernel;Username=leankernel;Password=leankernel"
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDbContextFactory<LeanKernelDbContext>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ISessionStore>().Should().BeOfType<PostgresSessionStore>();
        provider.GetRequiredService<IDiagnosticsSink>().Should().BeOfType<PostgresDiagnosticsSink>();
    }

    [Fact]
    public async Task SessionBelongsToUserAsync_returns_true_when_session_belongs_to_user()
    {
        var factory = CreateFactory();
        var store = CreateStore(factory);

        var sessionId = await store.GetOrCreateSessionIdAsync("channel-1", "user-1");

        var belongs = await store.SessionBelongsToUserAsync(sessionId, "user-1");

        belongs.Should().BeTrue();
    }

    [Fact]
    public async Task SessionBelongsToUserAsync_returns_false_when_session_belongs_to_different_user()
    {
        var factory = CreateFactory();
        var store = CreateStore(factory);

        var sessionId = await store.GetOrCreateSessionIdAsync("channel-1", "user-1");

        var belongs = await store.SessionBelongsToUserAsync(sessionId, "user-2");

        belongs.Should().BeFalse();
    }

    [Fact]
    public async Task SessionBelongsToUserAsync_returns_false_for_nonexistent_session()
    {
        var factory = CreateFactory();
        var store = CreateStore(factory);

        var belongs = await store.SessionBelongsToUserAsync("nonexistent-session", "user-1");

        belongs.Should().BeFalse();
    }

    private static PostgresSessionStore CreateStore(TestDbContextFactory factory)
        => new(factory, NullLogger<PostgresSessionStore>.Instance);

    private static TestDbContextFactory CreateFactory()
        => new(new DbContextOptionsBuilder<LeanKernelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task SeedAsync(TestDbContextFactory factory, Action<LeanKernelDbContext> seed)
    {
        await using var db = await factory.CreateDbContextAsync();
        seed(db);
        await db.SaveChangesAsync();
    }

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
