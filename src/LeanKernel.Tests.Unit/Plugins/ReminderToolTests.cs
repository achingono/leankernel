using LeanKernel.Core.Interfaces;
using LeanKernel.Plugins.BuiltIn;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Plugins;

public class ReminderToolTests
{
    [Fact]
    public void Name_IsReminder()
    {
        var scheduler = Substitute.For<IScheduler>();
        var tool = new ReminderTool(scheduler);
        Assert.Equal("reminder", tool.Name);
    }

    [Fact]
    public async Task ExecuteAsync_ValidJson_SchedulesReminder()
    {
        var scheduler = Substitute.For<IScheduler>();
        var tool = new ReminderTool(scheduler);

        var json = """{"message":"Take medicine","cron":"0 9 * * *","id":"med1"}""";
        var result = await tool.ExecuteAsync(json, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("med1", result.Output);
        Assert.Contains("Take medicine", result.Output);
        await scheduler.Received(1).ScheduleAsync(
            "reminder-med1",
            "0 9 * * *",
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsError()
    {
        var scheduler = Substitute.For<IScheduler>();
        var tool = new ReminderTool(scheduler);

        var result = await tool.ExecuteAsync("not json", CancellationToken.None);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_MissingField_ReturnsError()
    {
        var scheduler = Substitute.For<IScheduler>();
        var tool = new ReminderTool(scheduler);

        var result = await tool.ExecuteAsync("""{"message":"test"}""", CancellationToken.None);
        Assert.False(result.Success);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var scheduler = Substitute.For<IScheduler>();
        var tool = new ReminderTool(scheduler);
        Assert.NotEmpty(tool.Description);
    }

    [Fact]
    public void ParametersSchema_ContainsRequiredFields()
    {
        var scheduler = Substitute.For<IScheduler>();
        var tool = new ReminderTool(scheduler);
        Assert.Contains("message", tool.ParametersSchema);
        Assert.Contains("cron", tool.ParametersSchema);
        Assert.Contains("id", tool.ParametersSchema);
    }

    [Fact]
    public async Task ExecuteAsync_ToolName_SetInResult()
    {
        var scheduler = Substitute.For<IScheduler>();
        var tool = new ReminderTool(scheduler);
        var json = """{"message":"test","cron":"* * * * *","id":"x"}""";
        var result = await tool.ExecuteAsync(json, CancellationToken.None);
        Assert.Equal("reminder", result.ToolName);
    }

    [Fact]
    public async Task ExecuteAsync_Duration_IsSet()
    {
        var scheduler = Substitute.For<IScheduler>();
        var tool = new ReminderTool(scheduler);
        var json = """{"message":"test","cron":"* * * * *","id":"x"}""";
        var result = await tool.ExecuteAsync(json, CancellationToken.None);
        Assert.True(result.Duration >= TimeSpan.Zero);
    }
}
