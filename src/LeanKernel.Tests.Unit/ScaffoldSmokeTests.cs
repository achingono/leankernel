using FluentAssertions;

namespace LeanKernel.Tests.Unit;

public class ScaffoldSmokeTests
{
    [Fact]
    public void Project_markers_match_expected_names()
    {
        typeof(LeanKernel.Abstractions.Configuration.LeanKernelConfig).Assembly.GetName().Name.Should().Be("LeanKernel.Abstractions");
        typeof(LeanKernel.Agents.AgentRuntime).Assembly.GetName().Name.Should().Be("LeanKernel.Agents");
        typeof(LeanKernel.Context.ContextGatekeeper).Assembly.GetName().Name.Should().Be("LeanKernel.Context");
        typeof(LeanKernel.Knowledge.GBrainMcpClient).Assembly.GetName().Name.Should().Be("LeanKernel.Knowledge");
        typeof(LeanKernel.Tools.ToolRegistry).Assembly.GetName().Name.Should().Be("LeanKernel.Tools");
        typeof(LeanKernel.Persistence.LeanKernelDbContext).Assembly.GetName().Name.Should().Be("LeanKernel.Persistence");
        typeof(LeanKernel.Diagnostics.DiagnosticsCollector).Assembly.GetName().Name.Should().Be("LeanKernel.Diagnostics");
        typeof(LeanKernel.Scheduler.SchedulerHostedService).Assembly.GetName().Name.Should().Be("LeanKernel.Scheduler");
    }
}
