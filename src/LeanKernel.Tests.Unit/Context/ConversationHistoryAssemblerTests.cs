using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Context;

public class ConversationHistoryAssemblerTests
{
    [Fact]
    public void Assemble_returns_empty_for_empty_history()
    {
        var assembler = CreateAssembler();

        var assembled = assembler.Assemble([], budgetTokens: 10);

        assembled.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Assemble_returns_empty_when_budget_is_not_positive(int budgetTokens)
    {
        var assembler = CreateAssembler();
        var history = new[]
        {
            new ConversationTurn { Role = "user", Content = "1234" }
        };

        var assembled = assembler.Assemble(history, budgetTokens);

        assembled.Should().BeEmpty();
    }

    [Fact]
    public void Assemble_keeps_newest_turns_in_chronological_order_within_budget()
    {
        var assembler = CreateAssembler();
        var history = new[]
        {
            new ConversationTurn { Role = "user", Content = "1234" },
            new ConversationTurn { Role = "assistant", Content = "5678" },
            new ConversationTurn { Role = "user", Content = "90ab" },
        };

        var assembled = assembler.Assemble(history, budgetTokens: 2);

        assembled.Select(turn => turn.Content).Should().Equal("5678", "90ab");
    }

    [Fact]
    public void Assemble_returns_empty_when_the_newest_turn_alone_exceeds_budget()
    {
        var assembler = CreateAssembler();
        var history = new[]
        {
            new ConversationTurn { Role = "user", Content = "1234" },
            new ConversationTurn { Role = "assistant", Content = "123456789" },
        };

        var assembled = assembler.Assemble(history, budgetTokens: 2);

        assembled.Should().BeEmpty();
    }

    [Fact]
    public async Task AssembleAsync_uses_legacy_truncation_when_history_shaping_is_disabled()
    {
        var assembler = CreateAssembler();
        var history = new[]
        {
            new ConversationTurn { Role = "user", Content = "1234", TurnId = "t1" },
            new ConversationTurn { Role = "assistant", Content = "5678", TurnId = "t2" },
            new ConversationTurn { Role = "user", Content = "90ab", TurnId = "t3" },
        };

        var assembled = await assembler.AssembleAsync("session-1", history, budgetTokens: 2);

        assembled.History.Select(turn => turn.Content).Should().Equal("5678", "90ab");
        assembled.Diagnostics.VerbatimTurns.Should().Be(2);
        assembled.Diagnostics.DroppedTurns.Should().Be(1);
        assembled.Diagnostics.TotalTokensAfter.Should().Be(2);
    }

    private static ConversationHistoryAssembler CreateAssembler()
        => new(
            new SimpleTokenEstimator(),
            Options.Create(new ContextConfig()),
            NullLogger<ConversationHistoryAssembler>.Instance);
}
