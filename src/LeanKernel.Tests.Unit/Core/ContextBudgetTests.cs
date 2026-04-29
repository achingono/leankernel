using LeanKernel.Core.Models;
using Xunit;

namespace LeanKernel.Tests.Unit.CoreTests;

public class ContextBudgetTests
{
    [Fact]
    public void FromModelWindow_Reserves25PercentForResponse()
    {
        var budget = ContextBudget.FromModelWindow(100_000);
        Assert.Equal(75_000, budget.TotalTokens);
    }

    [Fact]
    public void SystemPromptBudget_Is15Percent()
    {
        var budget = new ContextBudget { TotalTokens = 10_000 };
        Assert.Equal(1_500, budget.SystemPromptBudget);
    }

    [Fact]
    public void WikiFactsBudget_Is20Percent()
    {
        var budget = new ContextBudget { TotalTokens = 10_000 };
        Assert.Equal(2_000, budget.WikiFactsBudget);
    }

    [Fact]
    public void ConversationBudget_Is40Percent()
    {
        var budget = new ContextBudget { TotalTokens = 10_000 };
        Assert.Equal(4_000, budget.ConversationBudget);
    }

    [Fact]
    public void RetrievalBudget_Is20Percent()
    {
        var budget = new ContextBudget { TotalTokens = 10_000 };
        Assert.Equal(2_000, budget.RetrievalBudget);
    }

    [Fact]
    public void ToolsBudget_Is5Percent()
    {
        var budget = new ContextBudget { TotalTokens = 10_000 };
        Assert.Equal(500, budget.ToolsBudget);
    }

    [Fact]
    public void AllBudgets_SumToTotalTokens()
    {
        var budget = new ContextBudget { TotalTokens = 10_000 };
        var sum = budget.SystemPromptBudget + budget.WikiFactsBudget
                + budget.ConversationBudget + budget.RetrievalBudget
                + budget.ToolsBudget;
        Assert.Equal(budget.TotalTokens, sum);
    }

    [Fact]
    public void FromModelWindow_SmallWindow_Calculates()
    {
        var budget = ContextBudget.FromModelWindow(4_000);
        Assert.Equal(3_000, budget.TotalTokens);
    }
}
