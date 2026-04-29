using LeanKernel.Core.Interfaces;
using LeanKernel.Plugins;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Plugins;

public class PluginHostTests
{
    [Fact]
    public void Constructor_RegistersAllTools()
    {
        var t1 = CreateTool("tool1");
        var t2 = CreateTool("tool2");
        var host = new PluginHost([t1, t2]);

        Assert.Equal(2, host.Tools.Count);
    }

    [Fact]
    public void GetTool_ReturnsRegisteredTool()
    {
        var tool = CreateTool("wiki_query");
        var host = new PluginHost([tool]);

        Assert.Same(tool, host.GetTool("wiki_query"));
    }

    [Fact]
    public void GetTool_Unknown_ReturnsNull()
    {
        var host = new PluginHost([]);
        Assert.Null(host.GetTool("nonexistent"));
    }

    [Fact]
    public void GetToolNames_ReturnsAllNames()
    {
        var t1 = CreateTool("a");
        var t2 = CreateTool("b");
        var host = new PluginHost([t1, t2]);

        var names = host.GetToolNames().ToList();
        Assert.Contains("a", names);
        Assert.Contains("b", names);
    }

    [Fact]
    public void Tools_Dictionary_IsReadOnly()
    {
        var host = new PluginHost([CreateTool("x")]);
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, ITool>>(host.Tools);
    }

    private static ITool CreateTool(string name)
    {
        var tool = Substitute.For<ITool>();
        tool.Name.Returns(name);
        return tool;
    }
}
