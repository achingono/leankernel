using NSubstitute;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.BuiltIn;

namespace LeanKernel.Tests.Unit.Plugins;

public class ScheduledJobsToolTests
{
    [Fact]
    public void MetadataProperties_AreExposed()
    {
        var tool = new ScheduledJobsTool(
            Substitute.For<IScheduledJobManager>(),
            Substitute.For<IChatExecutionContextAccessor>());

        Assert.Equal("scheduled_jobs", tool.Name);
        Assert.Equal("scheduling", tool.Category);
        Assert.Contains("operation", tool.ParametersSchema);
        Assert.Equal(8, tool.Operations.Count);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutChatContext_ReturnsError()
    {
        var manager = Substitute.For<IScheduledJobManager>();
        var contextAccessor = Substitute.For<IChatExecutionContextAccessor>();
        contextAccessor.Current.Returns((ChatExecutionContext?)null);

        var tool = new ScheduledJobsTool(manager, contextAccessor);
        var result = await tool.ExecuteAsync("""{"operation":"list_jobs"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("chat execution context", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_CreateJob_UsesCurrentActorContext()
    {
        var manager = Substitute.For<IScheduledJobManager>();
        var contextAccessor = Substitute.For<IChatExecutionContextAccessor>();
        contextAccessor.Current.Returns(new ChatExecutionContext
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        });

        manager.CreateAsync(Arg.Any<ScheduledJobCreateRequest>(), Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<ScheduledJobCreateRequest>();
                var actor = callInfo.Arg<ScheduledJobActor>();
                return new ScheduledJobView
                {
                    Definition = new ScheduledJobDefinition
                    {
                        Id = "job-a",
                        Name = request.Name,
                        Enabled = true,
                        ScheduleKind = request.ScheduleKind,
                        CronExpression = request.CronExpression,
                        RunAtUtc = request.RunAtUtc,
                        TimeZoneId = request.TimeZoneId ?? "UTC",
                        ExecutionTimeoutSeconds = request.ExecutionTimeoutSeconds ?? 300,
                        OverlapPolicy = request.OverlapPolicy ?? ScheduledJobOverlapPolicy.Skip,
                        AgentId = request.AgentId ?? "main",
                        SessionKey = request.SessionKey,
                        SessionTarget = request.SessionTarget ?? "isolated",
                        WakeMode = request.WakeMode ?? "now",
                        PayloadMessage = request.PayloadMessage,
                        DeliveryChannel = request.DeliveryChannel ?? actor.ChannelId,
                        DeliveryRecipient = request.DeliveryRecipient ?? actor.UserId,
                        DeliveryMode = request.DeliveryMode ?? "announce",
                        Scope = request.Scope ?? ScheduledJobScope.Scoped,
                        OwnerUserId = actor.UserId,
                        OwnerChannelId = actor.ChannelId,
                        OwnerSessionId = actor.SessionId
                    },
                    State = new ScheduledJobState()
                };
            });

        var tool = new ScheduledJobsTool(manager, contextAccessor);
        var result = await tool.ExecuteAsync(
            """{"operation":"create_job","id":"job-a","name":"Morning Brief","message":"Do standup","cron":"0 7 * * *","timezone":"UTC"}""",
            CancellationToken.None);

        Assert.True(result.Success);
        await manager.Received(1).CreateAsync(
            Arg.Is<ScheduledJobCreateRequest>(r =>
                r.Id == "job-a" &&
                r.Name == "Morning Brief" &&
                r.PayloadMessage == "Do standup" &&
                r.CronExpression == "0 7 * * *"),
            Arg.Is<ScheduledJobActor>(a =>
                a.UserId == "user-a" &&
                a.ChannelId == "signal" &&
                a.SessionId == "sess-a" &&
                !a.IsAdmin),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ListJobs_ReturnsSerializedResults()
    {
        var manager = Substitute.For<IScheduledJobManager>();
        var contextAccessor = Substitute.For<IChatExecutionContextAccessor>();
        contextAccessor.Current.Returns(new ChatExecutionContext
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        });

        manager.ListAsync(Arg.Any<ScheduledJobListOptions>(), Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>())
            .Returns([
                new ScheduledJobView
                {
                    Definition = new ScheduledJobDefinition
                    {
                        Id = "job-a",
                        Name = "A",
                        PayloadMessage = "hello",
                        DeliveryChannel = "signal",
                        DeliveryRecipient = "user-a",
                        OwnerUserId = "user-a",
                        OwnerChannelId = "signal"
                    },
                    State = new ScheduledJobState()
                }
            ]);

        var tool = new ScheduledJobsTool(manager, contextAccessor);
        var result = await tool.ExecuteAsync("""{"operation":"list_jobs"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("\"count\": 1", result.Output);
        Assert.Contains("\"job-a\"", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_MissingOrEmptyOperation_ReturnsError()
    {
        var manager = Substitute.For<IScheduledJobManager>();
        var contextAccessor = Substitute.For<IChatExecutionContextAccessor>();
        contextAccessor.Current.Returns(new ChatExecutionContext
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        });

        var tool = new ScheduledJobsTool(manager, contextAccessor);
        var missing = await tool.ExecuteAsync("""{}""", CancellationToken.None);
        var empty = await tool.ExecuteAsync("""{"operation":"   "}""", CancellationToken.None);

        Assert.False(missing.Success);
        Assert.False(empty.Success);
        Assert.Contains("Missing required", missing.Error ?? "");
        Assert.Contains("required", empty.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_UpdateDeleteEnableDisableTrigger_AreRouted()
    {
        var manager = Substitute.For<IScheduledJobManager>();
        var contextAccessor = Substitute.For<IChatExecutionContextAccessor>();
        contextAccessor.Current.Returns(new ChatExecutionContext
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = true
        });

        var view = new ScheduledJobView
        {
            Definition = new ScheduledJobDefinition
            {
                Id = "job-a",
                Name = "A",
                PayloadMessage = "hello",
                DeliveryChannel = "signal",
                DeliveryRecipient = "user-a",
                OwnerUserId = "user-a",
                OwnerChannelId = "signal"
            },
            State = new ScheduledJobState()
        };

        manager.UpdateAsync(Arg.Any<string>(), Arg.Any<ScheduledJobUpdateRequest>(), Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>())
            .Returns(view);
        manager.SetEnabledAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>())
            .Returns(view);
        manager.TriggerAsync(Arg.Any<string>(), Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>())
            .Returns(view);

        var tool = new ScheduledJobsTool(manager, contextAccessor);

        var updateResult = await tool.ExecuteAsync(
            """{"operation":"update_job","jobId":"job-a","scheduleKind":"at","runAtUtc":"2026-06-01T12:00:00Z","scope":"global","scopeReason":"ops"}""",
            CancellationToken.None);
        var deleteResult = await tool.ExecuteAsync("""{"operation":"delete_job","jobId":"job-a"}""", CancellationToken.None);
        var enableResult = await tool.ExecuteAsync("""{"operation":"enable_job","jobId":"job-a"}""", CancellationToken.None);
        var disableResult = await tool.ExecuteAsync("""{"operation":"disable_job","jobId":"job-a"}""", CancellationToken.None);
        var triggerResult = await tool.ExecuteAsync("""{"operation":"trigger_job","jobId":"job-a"}""", CancellationToken.None);
        var getResult = await tool.ExecuteAsync("""{"operation":"get_job","jobId":"job-a"}""", CancellationToken.None);

        Assert.True(updateResult.Success);
        Assert.True(deleteResult.Success);
        Assert.True(enableResult.Success);
        Assert.True(disableResult.Success);
        Assert.True(triggerResult.Success);
        Assert.True(getResult.Success);

        await manager.Received(1).UpdateAsync(
            "job-a",
            Arg.Is<ScheduledJobUpdateRequest>(r =>
                r.ScheduleKind == ScheduledJobScheduleKind.At &&
                r.RunAtUtc.HasValue &&
                r.Scope == ScheduledJobScope.Global),
            Arg.Any<ScheduledJobActor>(),
            Arg.Any<CancellationToken>());
        await manager.Received(1).DeleteAsync("job-a", Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>());
        await manager.Received(1).SetEnabledAsync("job-a", true, Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>());
        await manager.Received(1).SetEnabledAsync("job-a", false, Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>());
        await manager.Received(1).TriggerAsync("job-a", Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>());
        await manager.Received(1).GetAsync("job-a", Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidOperation_ReturnsError()
    {
        var manager = Substitute.For<IScheduledJobManager>();
        var contextAccessor = Substitute.For<IChatExecutionContextAccessor>();
        contextAccessor.Current.Returns(new ChatExecutionContext
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        });

        var tool = new ScheduledJobsTool(manager, contextAccessor);
        var result = await tool.ExecuteAsync("""{"operation":"unsupported"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Unsupported operation", result.Error ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_GetJob_WhenMissing_ReturnsNotFoundMessage()
    {
        var manager = Substitute.For<IScheduledJobManager>();
        var contextAccessor = Substitute.For<IChatExecutionContextAccessor>();
        contextAccessor.Current.Returns(new ChatExecutionContext
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        });

        manager.GetAsync(Arg.Any<string>(), Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>())
            .Returns((ScheduledJobView?)null);

        var tool = new ScheduledJobsTool(manager, contextAccessor);
        var result = await tool.ExecuteAsync("""{"operation":"get_job","jobId":"missing"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("not found", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ListJobs_ParsesOptions()
    {
        var manager = Substitute.For<IScheduledJobManager>();
        var contextAccessor = Substitute.For<IChatExecutionContextAccessor>();
        contextAccessor.Current.Returns(new ChatExecutionContext
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = true
        });

        manager.ListAsync(Arg.Any<ScheduledJobListOptions>(), Arg.Any<ScheduledJobActor>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var tool = new ScheduledJobsTool(manager, contextAccessor);
        var result = await tool.ExecuteAsync(
            """{"operation":"list_jobs","includeDisabled":false,"includeAll":true}""",
            CancellationToken.None);

        Assert.True(result.Success);
        await manager.Received(1).ListAsync(
            Arg.Is<ScheduledJobListOptions>(o => !o.IncludeDisabled && o.IncludeAllJobs),
            Arg.Any<ScheduledJobActor>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("""{"operation":"create_job","name":"A","message":"B"}""", "either 'cron' or 'runAtUtc'")]
    [InlineData("""{"operation":"create_job","name":"A","message":"B","runAtUtc":"not-a-date"}""", "ISO datetime")]
    [InlineData("""{"operation":"create_job","name":"A","message":"B","cron":"* * * * *","scope":"bad"}""", "scope must be either")]
    [InlineData("""{"operation":"create_job","name":"A","message":"B","cron":"* * * * *","overlapPolicy":"bad"}""", "overlapPolicy must be either")]
    [InlineData("""{"operation":"update_job","jobId":"job-a","scheduleKind":"bad"}""", "scheduleKind must be either")]
    public async Task ExecuteAsync_InvalidInputs_ReturnsOperationErrors(string input, string expectedError)
    {
        var manager = Substitute.For<IScheduledJobManager>();
        var contextAccessor = Substitute.For<IChatExecutionContextAccessor>();
        contextAccessor.Current.Returns(new ChatExecutionContext
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = true
        });

        var tool = new ScheduledJobsTool(manager, contextAccessor);
        var result = await tool.ExecuteAsync(input, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
