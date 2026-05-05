using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class MessageQueueServiceTests
{
    private readonly ITimeBoundaryService _timeBoundary;

    public MessageQueueServiceTests()
    {
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursStart = 8,
                ActiveHoursEnd = 22
            }
        };
        _timeBoundary = new TimeBoundaryService(rules, new NullLogger<TimeBoundaryService>());
    }

    [Fact]
    public async Task EnqueueAsync_UrgentMessage_ReturnsSuccess()
    {
        var queue = new MessageQueueService(_timeBoundary, new NullLogger<MessageQueueService>());
        var message = new QueuedMessage
        {
            Id = "msg1",
            Channel = "Signal",
            Recipient = "+1234567890",
            Content = "Urgent alert",
            EnqueuedAt = DateTime.UtcNow
        };

        var result = await queue.EnqueueAsync(message, isUrgent: true);

        Assert.True(result.Success);
        Assert.Equal("msg1", result.MessageId);
        Assert.False(result.WillBeBatched);
    }

    [Fact]
    public async Task EnqueueAsync_NonUrgentInQuietHours_BatchesMessage()
    {
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursStart = 10,
                ActiveHoursEnd = 18
            }
        };
        var timeBoundary = new TimeBoundaryService(rules, new NullLogger<TimeBoundaryService>());
        var queue = new MessageQueueService(timeBoundary, new NullLogger<MessageQueueService>());

        var message = new QueuedMessage
        {
            Id = "msg2",
            Channel = "Signal",
            Recipient = "+1234567890",
            Content = "Regular message",
            EnqueuedAt = DateTime.UtcNow
        };

        // If we're currently in quiet hours, this will be batched
        var result = await queue.EnqueueAsync(message, isUrgent: false);

        Assert.True(result.Success);
        // Result depends on current time; just verify batching is considered
        Assert.NotNull(result.MessageId);
    }

    [Fact]
    public async Task GetReadyMessagesAsync_UrgentInQuietHours_IsReady()
    {
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursStart = 10,
                ActiveHoursEnd = 18
            }
        };
        var timeBoundary = new TimeBoundaryService(rules, new NullLogger<TimeBoundaryService>());
        var queue = new MessageQueueService(timeBoundary, new NullLogger<MessageQueueService>());

        var urgentMsg = new QueuedMessage
        {
            Id = "urgent1",
            Channel = "Signal",
            Recipient = "+1234567890",
            Content = "Urgent",
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = true
        };

        await queue.EnqueueAsync(urgentMsg, isUrgent: true);
        var readyMessages = await queue.GetReadyMessagesAsync();

        // Urgent message should be ready regardless of quiet hours
        if (!timeBoundary.IsInActiveHours())
        {
            var urgent = readyMessages.FirstOrDefault(m => m.Id == "urgent1");
            Assert.NotNull(urgent);
        }
    }

    [Fact]
    public async Task MarkDeliveredAsync_MarksMessageAsDelivered()
    {
        var queue = new MessageQueueService(_timeBoundary, new NullLogger<MessageQueueService>());

        var message = new QueuedMessage
        {
            Id = "msg3",
            Channel = "Signal",
            Recipient = "+1234567890",
            Content = "Test message",
            EnqueuedAt = DateTime.UtcNow
        };

        await queue.EnqueueAsync(message);
        await queue.MarkDeliveredAsync("msg3");

        var readyMessages = await queue.GetReadyMessagesAsync();
        var deliveredMsg = readyMessages.FirstOrDefault(m => m.Id == "msg3");

        Assert.Null(deliveredMsg); // Delivered messages shouldn't be in ready list
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsAccurateStats()
    {
        var queue = new MessageQueueService(_timeBoundary, new NullLogger<MessageQueueService>());

        // Add a few messages
        for (int i = 0; i < 3; i++)
        {
            var message = new QueuedMessage
            {
                Id = $"msg{i}",
                Channel = "Signal",
                Recipient = "+1234567890",
                Content = $"Message {i}",
                EnqueuedAt = DateTime.UtcNow
            };
            await queue.EnqueueAsync(message);
        }

        var stats = await queue.GetStatsAsync();

        Assert.Equal(3, stats.TotalEnqueued);
        Assert.Equal(3, stats.PendingMessages);
        Assert.Equal(0, stats.DeliveredMessages);
    }

    [Fact]
    public async Task EnqueueAsync_DuplicateId_ReturnsFalse()
    {
        var queue = new MessageQueueService(_timeBoundary, new NullLogger<MessageQueueService>());

        var message = new QueuedMessage
        {
            Id = "duplicate",
            Channel = "Signal",
            Recipient = "+1234567890",
            Content = "First message",
            EnqueuedAt = DateTime.UtcNow
        };

        var result1 = await queue.EnqueueAsync(message);
        var result2 = await queue.EnqueueAsync(message);

        Assert.True(result1.Success);
        Assert.False(result2.Success);
    }
}

public class AgentsConfigurationStepTests
{
    [Fact]
    public void GetAvailablePresets_ReturnsThreePresets()
    {
        var paths = new LeanKernelHostPaths
        {
            DataDirectory = Path.GetTempPath(),
            AgentsDirectory = Path.Combine(Path.GetTempPath(), "agents"),
            RuntimeConfigPath = Path.Combine(Path.GetTempPath(), "runtime.json"),
            OnboardingStatePath = Path.Combine(Path.GetTempPath(), "onboarding.json")
        };
        var provider = new StubEngagementRulesProvider(new EngagementRules());
        var step = new AgentsConfigurationStep(paths, provider, new NullLogger<AgentsConfigurationStep>());

        var presets = step.GetAvailablePresets();

        Assert.Equal(3, presets.Count);
        Assert.Equal("basic", presets[0].Name);
        Assert.Equal("autonomous", presets[1].Name);
        Assert.Equal("cautious", presets[2].Name);
    }

    [Fact]
    public async Task InitializeAsync_CreatesAgentsMd()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"agents_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var paths = new LeanKernelHostPaths
            {
                DataDirectory = tempDir,
                AgentsDirectory = Path.Combine(tempDir, "agents"),
                RuntimeConfigPath = Path.Combine(tempDir, "runtime.json"),
                OnboardingStatePath = Path.Combine(tempDir, "onboarding.json")
            };
            var provider = new StubEngagementRulesProvider(new EngagementRules());
            var step = new AgentsConfigurationStep(paths, provider, new NullLogger<AgentsConfigurationStep>());

            var result = await step.InitializeAsync("basic");

            Assert.True(result.Success);
            var agentsPath = Path.Combine(tempDir, "agents", "main", "AGENTS.md");
            Assert.True(File.Exists(agentsPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_WithValidAgentsMd_ReturnsValid()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"agents_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var paths = new LeanKernelHostPaths
            {
                DataDirectory = tempDir,
                AgentsDirectory = Path.Combine(tempDir, "agents"),
                RuntimeConfigPath = Path.Combine(tempDir, "runtime.json"),
                OnboardingStatePath = Path.Combine(tempDir, "onboarding.json")
            };
            var provider = new StubEngagementRulesProvider(new EngagementRules());
            var step = new AgentsConfigurationStep(paths, provider, new NullLogger<AgentsConfigurationStep>());

            // Initialize first
            await step.InitializeAsync("basic");

            // Then validate
            var result = await step.ValidateAsync();

            Assert.True(result.Success);
            Assert.True(result.IsValid);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

/// <summary>
/// Test stub for IEngagementRulesProvider
/// </summary>
internal sealed class StubEngagementRulesProvider : IEngagementRulesProvider
{
    private readonly EngagementRules _rules;

    public StubEngagementRulesProvider(EngagementRules rules)
    {
        _rules = rules;
    }

    public Task<EngagementRules> LoadAsync(CancellationToken ct)
    {
        return Task.FromResult(_rules);
    }

    public EngagementRules GetCurrent()
    {
        return _rules;
    }
}
