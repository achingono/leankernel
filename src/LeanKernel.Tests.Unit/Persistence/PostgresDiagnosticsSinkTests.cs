using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Persistence;

public class PostgresDiagnosticsSinkTests
{
    [Fact]
    public async Task RecordAsync_and_GetEntriesAsync_persist_and_round_trip_entries()
    {
        var factory = CreateFactory();
        var sink = new PostgresDiagnosticsSink(factory, NullLogger<PostgresDiagnosticsSink>.Instance);

        await sink.RecordAsync(new DiagnosticEntry
        {
            Id = "d2",
            SessionId = "session-1",
            TurnId = "turn-2",
            Category = "gateway",
            Payload = new { message = "later" },
            Timestamp = DateTimeOffset.Parse("2025-05-20T10:02:00Z")
        });
        await sink.RecordAsync(new DiagnosticEntry
        {
            Id = "d1",
            SessionId = "session-1",
            TurnId = "turn-1",
            Category = "gateway",
            Payload = new { message = "earlier" },
            Timestamp = DateTimeOffset.Parse("2025-05-20T10:01:00Z")
        });

        var entries = await sink.GetEntriesAsync("session-1");

        entries.Select(entry => entry.Id).Should().Equal("d1", "d2");
        var payload = (JsonElement)entries[0].Payload;
        payload.GetProperty("message").GetString().Should().Be("earlier");
    }

    private static TestDbContextFactory CreateFactory()
        => new(new DbContextOptionsBuilder<LeanKernelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

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
