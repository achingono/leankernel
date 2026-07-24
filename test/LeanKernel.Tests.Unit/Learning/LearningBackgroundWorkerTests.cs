using System.Reflection;
using System.Text.Json;

using FluentAssertions;

using LeanKernel;
using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Events;
using LeanKernel.Logic.Events;
using LeanKernel.Services.Learning;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Learning;

public sealed class LearningBackgroundWorkerTests
{
    [Fact]
    public async Task PollAndEnqueueTurnEventsAsync_ValidPayload_EnqueuesAndUpdatesCheckpoint()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var turn = CreateTurnEvent();
        await using (var seed = await fixture.ContextFactory.CreateDbContextAsync())
        {
            seed.Events.Add(new EventEntity
            {
                Id = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                EventType = "turn_completed",
                RecordType = "LeanKernel.Events.TurnCompletedEvent",
                TenantId = turn.Envelope.TenantId,
                PersonId = turn.Envelope.PersonId,
                UserId = turn.Envelope.UserId,
                ChannelId = turn.Envelope.ChannelId,
                PayloadJson = JsonSerializer.Serialize(turn, Constants.Serialization.JsonOptions),
                CreatedOn = DateTime.UtcNow,
                Timestamp = DateTimeOffset.UtcNow,
            });

            await seed.SaveChangesAsync();
        }

        var producer = new Mock<ITurnEventProducer>();
        var worker = fixture.CreateWorker(producer.Object);

        await InvokePrivateAsync(worker, "PollAndEnqueueTurnEventsAsync", CancellationToken.None);

        producer.Verify(x => x.EnqueueAsync(It.IsAny<TurnCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);

        await using var assertContext = await fixture.ContextFactory.CreateDbContextAsync();
        var checkpoint = await assertContext.LearningCheckpoints.SingleAsync();
        checkpoint.LastProcessedEventRowId.Should().NotBeNull();
        checkpoint.LastProcessedCreatedOnUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task PollAndEnqueueTurnEventsAsync_MalformedPayload_SkipsEvent()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await using (var seed = await fixture.ContextFactory.CreateDbContextAsync())
        {
            seed.Events.Add(new EventEntity
            {
                Id = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                EventType = "turn_completed",
                RecordType = "LeanKernel.Events.TurnCompletedEvent",
                TenantId = Guid.NewGuid(),
                PersonId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                ChannelId = Guid.NewGuid(),
                PayloadJson = "{ not-json",
                CreatedOn = DateTime.UtcNow,
                Timestamp = DateTimeOffset.UtcNow,
            });

            await seed.SaveChangesAsync();
        }

        var producer = new Mock<ITurnEventProducer>();
        var worker = fixture.CreateWorker(producer.Object);

        await InvokePrivateAsync(worker, "PollAndEnqueueTurnEventsAsync", CancellationToken.None);

        producer.Verify(x => x.EnqueueAsync(It.IsAny<TurnCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);

        await using var assertContext = await fixture.ContextFactory.CreateDbContextAsync();
        var checkpoint = await assertContext.LearningCheckpoints.SingleAsync();
        checkpoint.LastProcessedEventRowId.Should().BeNull();
    }

    private static async Task InvokePrivateAsync(object target, string methodName, CancellationToken ct)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = (Task?)method!.Invoke(target, [ct]);
        task.Should().NotBeNull();
        await task!;
    }

    private static TurnCompletedEvent CreateTurnEvent()
    {
        return new TurnCompletedEvent
        {
            Envelope = new EventEnvelope
            {
                EventType = "turn_completed",
                TenantId = Guid.NewGuid(),
                PersonId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                ChannelId = Guid.NewGuid(),
            },
            TurnId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            UserMessage = "hello",
            AssistantResponse = "world",
        };
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestFixture(SqliteConnection connection, TestContextFactory contextFactory)
        {
            _connection = connection;
            ContextFactory = contextFactory;
        }

        public TestContextFactory ContextFactory { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<EntityContext>()
                .UseSqlite(connection)
                .Options;

            var contextFactory = new TestContextFactory(options);
            await using var context = await contextFactory.CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();

            return new TestFixture(connection, contextFactory);
        }

        public LearningBackgroundWorker CreateWorker(ITurnEventProducer producer)
        {
            var services = new ServiceCollection();
            var provider = services.BuildServiceProvider();

            return new LearningBackgroundWorker(
                Mock.Of<ITurnEventConsumer>(),
                producer,
                ContextFactory,
                Mock.Of<IEventStore>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new LearningSettings { Enabled = true }),
                NullLogger<LearningBackgroundWorker>.Instance);
        }

        public ValueTask DisposeAsync()
        {
            return _connection.DisposeAsync();
        }
    }

    private sealed class TestContextFactory(DbContextOptions<EntityContext> options) : IDbContextFactory<EntityContext>
    {
        public EntityContext CreateDbContext()
        {
            return new EntityContext(options);
        }

        public Task<EntityContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EntityContext(options));
        }
    }
}
