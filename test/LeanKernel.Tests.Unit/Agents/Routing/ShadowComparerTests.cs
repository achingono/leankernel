using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Routing;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Routing;

public class ShadowComparerTests
{
    [Fact]
    public void Compare_returns_length_ratio_and_longer_note_when_shadow_is_significantly_longer()
    {
        var comparer = CreateComparer();

        var result = comparer.Compare("Short answer.", "This is a much longer shadow answer that includes substantially more implementation detail.");

        result.LengthRatio.Should().BeGreaterThan(1.5);
        result.BothNonEmpty.Should().BeTrue();
        result.PrimaryRefusal.Should().BeFalse();
        result.ShadowRefusal.Should().BeFalse();
        result.Notes.Should().Be("shadow significantly longer");
    }

    [Fact]
    public void Compare_detects_when_shadow_refuses_but_primary_does_not()
    {
        var comparer = CreateComparer();

        var result = comparer.Compare(
            "Here is the requested project status update with milestones and next steps.",
            "I cannot help with that request.");

        result.PrimaryRefusal.Should().BeFalse();
        result.ShadowRefusal.Should().BeTrue();
        result.Notes.Should().Contain("shadow refused but primary didn't");
    }

    [Fact]
    public void Compare_marks_both_non_empty_false_when_either_side_is_blank()
    {
        var comparer = CreateComparer();

        var result = comparer.Compare("Primary answer", string.Empty);

        result.BothNonEmpty.Should().BeFalse();
        result.LengthRatio.Should().Be(0);
    }

    private static ShadowComparer CreateComparer()
        => new(Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                RefusalPatterns = ["I cannot", "I'm sorry, I can't"]
            }
        }));
}
