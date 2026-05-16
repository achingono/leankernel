using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;
using LeanKernel.Thinker;
using Xunit;

namespace LeanKernel.Tests.Unit.Thinker;

public class PromptAssemblerFullTests
{
    private readonly PromptAssembler _assembler = new(NullLogger<PromptAssembler>.Instance);

    [Fact]
    public void Assemble_MinimalContext_ReturnsSystemPrompt()
    {
        var ctx = MakeContext("You are LeanKernel.");
        var result = _assembler.Assemble(ctx);
        Assert.Contains("You are LeanKernel.", result);
    }

    [Fact]
    public void Assemble_WithWikiLeanKernels_IncludesKnowledge()
    {
        var ctx = MakeContext("System", wikiLeanKernels:
        [
            new RelevanceScore { EntryId = "e1", Content = "Alice is a developer", EstimatedTokens = 5 }
        ]);
        var result = _assembler.Assemble(ctx);
        Assert.Contains("Wiki", result);
        Assert.Contains("Alice is a developer", result);
    }

    [Fact]
    public void Assemble_WithRetrievedLeanKernels_IncludesContext()
    {
        var ctx = MakeContext("System", retrievedLeanKernels:
        [
            new RelevanceScore { EntryId = "e2", Content = "RAG result", EstimatedTokens = 3 }
        ]);
        var result = _assembler.Assemble(ctx);
        Assert.Contains("Documents", result);
        Assert.Contains("RAG result", result);
    }

    [Fact]
    public void Assemble_WithHistory_IncludesConversation()
    {
        var ctx = MakeContext("System", history:
        [
            new ConversationTurn { Role = "user", Content = "Hello", Timestamp = DateTimeOffset.UtcNow },
            new ConversationTurn { Role = "assistant", Content = "Hi there", Timestamp = DateTimeOffset.UtcNow }
        ]);
        var result = _assembler.Assemble(ctx);
        Assert.Contains("Conversation", result);
        Assert.Contains("User: Hello", result);
        Assert.Contains("LeanKernel: Hi there", result);
    }

    [Fact]
    public void Assemble_CompactedTurn_ShowsMarker()
    {
        var ctx = MakeContext("System", history:
        [
            new ConversationTurn { Role = "user", Content = "Old msg", Timestamp = DateTimeOffset.UtcNow, IsCompacted = true }
        ]);
        var result = _assembler.Assemble(ctx);
        Assert.Contains("[compacted]", result);
    }

    [Fact]
    public void AssembleSystemMessage_MinimalContext()
    {
        var ctx = MakeContext("You are lean.");
        var result = _assembler.AssembleSystemMessage(ctx);
        Assert.Contains("You are lean.", result);
    }

    [Fact]
    public void AssembleSystemMessage_WithWikiLeanKernels_FormattedAsList()
    {
        var ctx = MakeContext("System", wikiLeanKernels:
        [
            new RelevanceScore { EntryId = "e1", Content = "Fact one", EstimatedTokens = 2 }
        ]);
        var result = _assembler.AssembleSystemMessage(ctx);
        Assert.Contains("## Wiki", result);
        Assert.Contains("- Fact one", result);
    }

    [Fact]
    public void AssembleSystemMessage_WithRetrievedLeanKernels()
    {
        var ctx = MakeContext("System", retrievedLeanKernels:
        [
            new RelevanceScore { EntryId = "e2", Content = "Context data", EstimatedTokens = 3 }
        ]);
        var result = _assembler.AssembleSystemMessage(ctx);
        Assert.Contains("## Documents", result);
        Assert.Contains("- Context data", result);
    }

    [Fact]
    public void AssembleSystemMessage_WithActiveTools()
    {
        var ctx = MakeContext("System", toolNames: ["search_wiki", "web_search"]);
        var result = _assembler.AssembleSystemMessage(ctx);
        Assert.Contains("## Available Tools", result);
        Assert.Contains("search_wiki", result);
        Assert.Contains("web_search", result);
    }

    [Fact]
    public void AssembleSystemMessage_WithDisambiguationHints_IncludesSection()
    {
        var ctx = MakeContext("System");
        ctx = ctx with
        {
            DisambiguationHints = ["I found 2 people named 'terry'. Ask which one they mean."]
        };

        var result = _assembler.AssembleSystemMessage(ctx);

        Assert.Contains("## Disambiguation", result);
        Assert.Contains("2 people named 'terry'", result);
    }

    [Fact]
    public void AssembleSystemMessage_HighPriorityWikiComesBeforeMedium()
    {
        var ctx = MakeContext("System", wikiLeanKernels:
        [
            new RelevanceScore
            {
                EntryId = "medium",
                Content = "Medium priority wiki fact",
                EstimatedTokens = 4,
                Priority = ContextPriority.Medium,
                Score = 0.9
            },
            new RelevanceScore
            {
                EntryId = "high",
                Content = "High priority wiki fact",
                EstimatedTokens = 4,
                Priority = ContextPriority.High,
                Score = 0.1
            }
        ]);

        var result = _assembler.AssembleSystemMessage(ctx);
        var highIndex = result.IndexOf("High priority wiki fact", StringComparison.Ordinal);
        var mediumIndex = result.IndexOf("Medium priority wiki fact", StringComparison.Ordinal);

        Assert.True(highIndex >= 0 && mediumIndex >= 0);
        Assert.True(highIndex < mediumIndex);
    }

    [Fact]
    public void AssembleSystemMessage_NoLeanKernels_NoSections()
    {
        var ctx = MakeContext("Just system.");
        var result = _assembler.AssembleSystemMessage(ctx);
        Assert.DoesNotContain("## Wiki", result);
        Assert.DoesNotContain("## Documents", result);
        Assert.DoesNotContain("## Available Tools", result);
    }

    [Fact]
    public void AssembleSystemMessage_StripsRawStoragePaths()
    {
        var ctx = MakeContext("System", retrievedLeanKernels:
        [
            new RelevanceScore { EntryId = "e2", Content = "See /app/data/documents/raw/file.pdf", EstimatedTokens = 4 }
        ]);

        var result = _assembler.AssembleSystemMessage(ctx);
        Assert.DoesNotContain("/app/data/documents", result);
        Assert.Contains("documents/raw/file.pdf", result);
    }

    private static ConversationContext MakeContext(
        string systemPrompt,
        List<RelevanceScore>? wikiLeanKernels = null,
        List<RelevanceScore>? retrievedLeanKernels = null,
        List<ConversationTurn>? history = null,
        List<string>? toolNames = null)
        => new()
        {
            SystemPrompt = systemPrompt,
            History = history ?? [],
            WikiLeanKernels = wikiLeanKernels ?? [],
            RetrievedLeanKernels = retrievedLeanKernels ?? [],
            ActiveToolNames = toolNames ?? []
        };
}
