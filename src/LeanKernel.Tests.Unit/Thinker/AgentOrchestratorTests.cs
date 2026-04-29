using LeanKernel.Thinker.Agents;

namespace LeanKernel.Tests.Unit.Thinker;

public class AgentOrchestratorTests
{
    [Theory]
    [InlineData("What time is it?", TaskComplexity.Simple)]
    [InlineData("Hello", TaskComplexity.Simple)]
    [InlineData("Research the latest trends in AI and then compare them", TaskComplexity.Complex)]
    [InlineData("Write code for a web server and then deploy it", TaskComplexity.Complex)]
    [InlineData("Investigate why the tests are failing", TaskComplexity.Complex)]
    public void AnalyzeComplexity_ClassifiesCorrectly(string query, TaskComplexity expected)
    {
        var result = AgentOrchestrator.AnalyzeComplexity(query);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AnalyzeComplexity_LongQueriesAreComplex()
    {
        var longQuery = new string('x', 600);
        var result = AgentOrchestrator.AnalyzeComplexity(longQuery);
        Assert.Equal(TaskComplexity.Complex, result);
    }

    [Fact]
    public void DecomposeTask_AssignsResearchWorker()
    {
        var plan = AgentOrchestrator.DecomposeTask("Research the history of computing");
        Assert.Contains(plan, p => p.Worker == "research");
    }

    [Fact]
    public void DecomposeTask_AssignsCodeWorker()
    {
        var plan = AgentOrchestrator.DecomposeTask("Write code for a REST API");
        Assert.Contains(plan, p => p.Worker == "code");
    }

    [Fact]
    public void DecomposeTask_AssignsScheduleWorker()
    {
        var plan = AgentOrchestrator.DecomposeTask("Schedule a reminder for tomorrow");
        Assert.Contains(plan, p => p.Worker == "schedule");
    }

    [Fact]
    public void DecomposeTask_DefaultsToResearch()
    {
        var plan = AgentOrchestrator.DecomposeTask("Tell me about quantum physics");
        Assert.NotEmpty(plan);
        Assert.Contains(plan, p => p.Worker == "research");
    }

    [Fact]
    public void DecomposeTask_MultipleWorkers()
    {
        var plan = AgentOrchestrator.DecomposeTask("Research best practices and write code for authentication");
        Assert.True(plan.Count >= 2);
    }
}
