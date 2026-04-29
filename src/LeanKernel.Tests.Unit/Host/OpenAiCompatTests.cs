using LeanKernel.Thinker.Agents;

namespace LeanKernel.Tests.Unit.Host;

public class OpenAiCompatTests
{
    [Fact]
    public void ComplexityAnalysis_SimpleQueries()
    {
        Assert.Equal(TaskComplexity.Simple, AgentOrchestrator.AnalyzeComplexity("Hi"));
        Assert.Equal(TaskComplexity.Simple, AgentOrchestrator.AnalyzeComplexity("What time is it?"));
    }

    [Fact]
    public void ComplexityAnalysis_ComplexQueries()
    {
        Assert.Equal(TaskComplexity.Complex,
            AgentOrchestrator.AnalyzeComplexity("Research the latest developments in quantum computing and then compare approaches"));
    }
}
