using FluentAssertions;

using LeanKernel.Logic.Memory;

using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

/// <summary>
/// Covers round-trip rendering and parsing of memory pages.
/// </summary>
public class MemoryPageRoundTripTests
{
    /// <summary>
    /// Verifies rendered learned pages preserve their core sections when parsed.
    /// </summary>
    [Fact]
    public void Render_Then_Parse_RoundTripsCoreSections()
    {
        var renderer = new MemoryPageRenderer();
        var parser = new MemoryPageParser();

        var markdown = renderer.RenderLearnedPage(new LearnedPageParameters(
            new Dictionary<string, string?>
            {
                ["Who"] = "Jane Doe",
                ["What"] = "Approved Q4 budget",
                ["When"] = "2026-07-10T12:00:00.0000000+00:00",
                ["Where"] = "Seattle office",
                ["Why"] = "Finalize budget",
                ["How"] = "Finance review"
            },
            "what",
            ["who", "where"],
            [new MemoryPageLink("facts/who/jane-doe/a1", "related", 90, ["same-session"])],
            "complete",
            "deterministic",
            [],
            "s1",
            "t1",
            DateTimeOffset.Parse("2026-07-10T12:00:00Z")));

        var snapshot = parser.Parse("facts/what/q4-budget/a1", markdown);

        snapshot.Fields["Who"].Should().Be("Jane Doe");
        snapshot.PrimaryDimension.Should().Be("what");
        snapshot.SecondaryDimensions.Should().ContainInOrder("who", "where");
        snapshot.Links.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies seed pages parse fact text and metadata correctly.
    /// </summary>
    [Fact]
    public void Parse_SeedPage_ExtractsFactAndMetadata()
    {
        var parser = new MemoryPageParser();
        var content = "# Learned Fact\n\nJane approved budget\n\n- Session: sess-1\n- Turn: turn-9\n- RecordedAt: 2026-07-10T12:00:00Z";

        var snapshot = parser.Parse("facts/what/jane-approved/x", content);

        snapshot.FactText.Should().Be("Jane approved budget");
        snapshot.SessionId.Should().Be("sess-1");
        snapshot.TurnId.Should().Be("turn-9");
        snapshot.Fields["What"].Should().BeNull();
    }
}