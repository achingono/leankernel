using System.Data.Common;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Memory;
using LeanKernel.Services.Learning.Onboarding;
using LeanKernel.Tests.Unit.TestDoubles;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Onboarding;

public sealed class OnboardingGapDetectorTests : IDisposable
{
    private readonly DbConnection _connection;
    private readonly DbContextOptions<EntityContext> _contextOptions;

    public OnboardingGapDetectorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _contextOptions = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new EntityContext(_contextOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task DetectGapsAsync_UserMissing_ReturnsProfileGap()
    {
        var memory = new Mock<IMemoryService>();
        var detector = CreateDetector(memory.Object);

        var gaps = await detector.DetectGapsAsync(Guid.NewGuid(), Guid.NewGuid());

        gaps.Should().ContainSingle(g => g.GapType == "UserProfileMissing");
    }

    [Fact]
    public async Task DetectGapsAsync_ProfileFieldsMissing_ReturnsExpectedGaps()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUserAsync(new UserEntity
        {
            Id = userId,
            Email = string.Empty,
            UserName = "user-a",
            FirstName = string.Empty,
            LastName = string.Empty,
            FullName = string.Empty,
            PreferredUserName = string.Empty,
            Locale = string.Empty,
            TimeZone = string.Empty,
            Organization = string.Empty,
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" },
        });

        var memory = new Mock<IMemoryService>();
        memory
            .Setup(m => m.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MemorySearchResult>());

        var detector = CreateDetector(memory.Object);
        var gaps = await detector.DetectGapsAsync(tenantId, userId);

        gaps.Select(g => g.GapType).Should().Contain([
            "FullNameMissing",
            "EmailMissing",
            "TimeZoneMissing",
            "LocaleMissing",
            "PreferencesUnknown",
        ]);
        gaps.Should().BeInDescendingOrder(g => g.Priority);
    }

    [Fact]
    public async Task DetectGapsAsync_CompleteProfileAndIntent_ReturnsNoGaps()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUserAsync(new UserEntity
        {
            Id = userId,
            Email = "user@example.com",
            UserName = "user-b",
            FirstName = "Jane",
            LastName = "Doe",
            FullName = "Jane Doe",
            PreferredUserName = "jane",
            Locale = "en-US",
            TimeZone = "America/Los_Angeles",
            Organization = "LeanKernel",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" },
        });

        var memory = new Mock<IMemoryService>();
        memory
            .Setup(m => m.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MemorySearchResult { Key = "identity/intent/test", Content = "prefers concise replies", Score = 0.9 },
            ]);

        var detector = CreateDetector(memory.Object);
        var gaps = await detector.DetectGapsAsync(tenantId, userId);

        gaps.Should().BeEmpty();
    }

    private OnboardingGapDetector CreateDetector(IMemoryService memoryService)
    {
        var factory = new TestDbContextFactory(_contextOptions);
        var logger = Mock.Of<ILogger<OnboardingGapDetector>>();
        return new OnboardingGapDetector(factory, memoryService, logger);
    }

    private async Task SeedUserAsync(UserEntity user)
    {
        using var context = new EntityContext(_contextOptions);
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }
}
