using LeanKernel.Core.Enums;

namespace LeanKernel.Tests.Unit.Archivist;

public class ContextGatekeeperTests
{
    [Theory]
    [InlineData("Who is Alice?", WikiDimension.Who)]
    [InlineData("What happened at the meeting?", WikiDimension.What)]
    [InlineData("Where is the office?", WikiDimension.Where)]
    [InlineData("When is the deadline?", WikiDimension.When)]
    [InlineData("Why did the build fail?", WikiDimension.Why)]
    [InlineData("How do I deploy this?", WikiDimension.How)]
    public void ClassifyDimensions_DetectsCorrectDimension(string query, WikiDimension expected)
    {
        var dims = LeanKernel.Archivist.ContextGatekeeper.ClassifyDimensions(query);
        Assert.Contains(expected, dims);
    }

    [Fact]
    public void ClassifyDimensions_DefaultsToWhoAndWhat_WhenNoDimensionDetected()
    {
        var dims = LeanKernel.Archivist.ContextGatekeeper.ClassifyDimensions("hello there");
        Assert.Contains(WikiDimension.Who, dims);
        Assert.Contains(WikiDimension.What, dims);
    }

    [Fact]
    public void ClassifyDimensions_DetectsMultipleDimensions()
    {
        var dims = LeanKernel.Archivist.ContextGatekeeper.ClassifyDimensions(
            "Who scheduled the meeting and when is it?");
        Assert.Contains(WikiDimension.Who, dims);
        Assert.Contains(WikiDimension.When, dims);
    }
}
