using System.Text.Json;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Diagnostics;
using LeanKernel.Services.Gateway.Requests;
using LeanKernel.Tests.Unit.TestDoubles;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using Xunit;

namespace LeanKernel.Tests.Unit.Diagnostics;

public class DiagnosticsEndpointHandlerTests : IDisposable
{
    private readonly EntityContext _dbContext;

    public DiagnosticsEndpointHandlerTests()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new EntityContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task HandleListEntriesAsync_ReturnsEntriesOrderedByCapturedAtDescending()
    {
        var earlier = new DiagnosticEntry
        {
            Source = "gateway",
            Category = "turn",
            PayloadJson = "{}",
            CapturedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        var later = new DiagnosticEntry
        {
            Source = "memory",
            Category = "read",
            PayloadJson = "{}",
            CapturedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.DiagnosticEntries.AddRange(earlier, later);
        await _dbContext.SaveChangesAsync();

        var result = await DiagnosticsEndpoint.HandleListEntriesAsync(_dbContext, take: 100, CancellationToken.None);

        var entries = await ReadBodyAsJsonArrayAsync(result);
        entries.Should().HaveCount(2);
        entries[0].GetProperty("source").GetString().Should().Be("memory");
        entries[1].GetProperty("source").GetString().Should().Be("gateway");
    }

    [Fact]
    public async Task HandleListEntriesAsync_RespectsTakeLimit()
    {
        for (var i = 0; i < 10; i++)
        {
            _dbContext.DiagnosticEntries.Add(new DiagnosticEntry
            {
                Source = "test",
                Category = "test",
                PayloadJson = "{}",
                CapturedAt = DateTimeOffset.UtcNow,
            });
        }

        await _dbContext.SaveChangesAsync();

        var result = await DiagnosticsEndpoint.HandleListEntriesAsync(_dbContext, take: 3, CancellationToken.None);

        var entries = await ReadBodyAsJsonArrayAsync(result);
        entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task HandleListEntriesAsync_ClampsTakeTo500()
    {
        for (var i = 0; i < 600; i++)
        {
            _dbContext.DiagnosticEntries.Add(new DiagnosticEntry
            {
                Source = "test",
                Category = "test",
                PayloadJson = "{}",
                CapturedAt = DateTimeOffset.UtcNow,
            });
        }

        await _dbContext.SaveChangesAsync();

        var result = await DiagnosticsEndpoint.HandleListEntriesAsync(_dbContext, take: 1000, CancellationToken.None);

        var entries = await ReadBodyAsJsonArrayAsync(result);
        entries.Should().HaveCount(500);
    }

    [Fact]
    public async Task HandleListEntriesAsync_UsesDefaultTakeWhenZero()
    {
        for (var i = 0; i < 10; i++)
        {
            _dbContext.DiagnosticEntries.Add(new DiagnosticEntry
            {
                Source = "test",
                Category = "test",
                PayloadJson = "{}",
                CapturedAt = DateTimeOffset.UtcNow,
            });
        }

        await _dbContext.SaveChangesAsync();

        var result = await DiagnosticsEndpoint.HandleListEntriesAsync(_dbContext, take: 0, CancellationToken.None);

        var entries = await ReadBodyAsJsonArrayAsync(result);
        entries.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HandleHealthAsync_ReturnsHealthStatus()
    {
        var service = new StubHealthCheckService(new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["litellm"] = new(HealthStatus.Healthy, "ok", TimeSpan.Zero, null, null),
            },
            HealthStatus.Healthy,
            TimeSpan.Zero));

        var aggregator = new HealthAggregator(service);

        var result = await DiagnosticsEndpoint.HandleHealthAsync(aggregator, CancellationToken.None);

        var json = await ReadBodyAsJsonElementAsync(result);
        json.GetProperty("healthy").GetBoolean().Should().BeTrue();
        json.GetProperty("litellmHealthy").GetBoolean().Should().BeTrue();
    }

    private static async Task<JsonElement[]> ReadBodyAsJsonArrayAsync(IResult result)
    {
        var json = await ReadBodyAsJsonElementAsync(result);
        return json.EnumerateArray().ToArray();
    }

    private static async Task<JsonElement> ReadBodyAsJsonElementAsync(IResult result)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        httpContext.RequestServices = CreateServices();
        await result.ExecuteAsync(httpContext);
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        return services.BuildServiceProvider();
    }
}