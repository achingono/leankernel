using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Enhancement;
using LeanKernel.Archivist.Engagement;

namespace LeanKernel.Tests.Unit.Thinker.Enhancement;

public class RefusalInterceptorResponseEnhancerTests
{
    [Fact]
    public async Task EnhanceResponseAsync_RefusalWithPermission_CreatesIdentityFiles()
    {
        var toolRegistry = Substitute.For<IToolRegistry>();
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        
        var fileWriteTool = Substitute.For<ITool>();
        fileWriteTool.Name.Returns("file_write");
        fileWriteTool.ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ToolResult { ToolName = "file_write", Success = true });
        
        var toolDict = new Dictionary<string, ITool> { { "file_write", fileWriteTool } };
        toolRegistry.Tools.Returns(toolDict);
        
        actionAuthorizer.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult { IsAuthorized = true, ActionType = "WriteSelfMd" });
        
        var enhancer = new RefusalInterceptorResponseEnhancer(toolRegistry, actionAuthorizer, NullLogger<RefusalInterceptorResponseEnhancer>.Instance);
        
        var userMessage = "I want you to create the files. You have my permission to create them.";
        var refusalResponse = """
            I am unable to directly create files on your local system. However, you can create the files manually.
            
            ### USER.md
            # User Profile
            ## Priorities
            1. Test priority
            """;
        
        var context = new ConversationContext
        {
            SystemPrompt = "test",
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };
        
        var result = await enhancer.EnhanceResponseAsync(userMessage, refusalResponse, context, CancellationToken.None);
        
        Assert.NotEqual(refusalResponse, result);
        Assert.Contains("✓ Created", result);
        Assert.Contains("agents/main/USER.md", result);
        await fileWriteTool.Received(1).ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnhanceResponseAsync_NoRefusal_PassesThroughUnchanged()
    {
        var toolRegistry = Substitute.For<IToolRegistry>();
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        
        var enhancer = new RefusalInterceptorResponseEnhancer(toolRegistry, actionAuthorizer, NullLogger<RefusalInterceptorResponseEnhancer>.Instance);
        
        var userMessage = "What time is it?";
        var normalResponse = "It is currently 3:00 PM.";
        var context = new ConversationContext
        {
            SystemPrompt = "test",
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };
        
        var result = await enhancer.EnhanceResponseAsync(userMessage, normalResponse, context, CancellationToken.None);
        
        Assert.Equal(normalResponse, result);
    }

    [Fact]
    public async Task EnhanceResponseAsync_RefusalWithoutPermission_PassesThroughUnchanged()
    {
        var toolRegistry = Substitute.For<IToolRegistry>();
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        
        var enhancer = new RefusalInterceptorResponseEnhancer(toolRegistry, actionAuthorizer, NullLogger<RefusalInterceptorResponseEnhancer>.Instance);
        
        var userMessage = "Can you create the files?";
        var refusalResponse = "I am unable to directly create files on your local system.";
        var context = new ConversationContext
        {
            SystemPrompt = "test",
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };
        
        var result = await enhancer.EnhanceResponseAsync(userMessage, refusalResponse, context, CancellationToken.None);
        
        Assert.Equal(refusalResponse, result);
    }

    [Fact]
    public async Task EnhanceResponseAsync_GenericProceedLanguage_DoesNotGrantPermission()
    {
        var toolRegistry = Substitute.For<IToolRegistry>();
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        var fileWriteTool = Substitute.For<ITool>();
        fileWriteTool.Name.Returns("file_write");
        toolRegistry.Tools.Returns(new Dictionary<string, ITool> { { "file_write", fileWriteTool } });
        var enhancer = new RefusalInterceptorResponseEnhancer(toolRegistry, actionAuthorizer, NullLogger<RefusalInterceptorResponseEnhancer>.Instance);
        var userMessage = "How should I proceed with testing? The docs mention USER.md.";
        var refusalResponse = """
            I am unable to directly create files on your local system.

            ### USER.md
            # User Profile
            """;
        var context = new ConversationContext
        {
            SystemPrompt = "test",
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };

        var result = await enhancer.EnhanceResponseAsync(userMessage, refusalResponse, context, CancellationToken.None);

        Assert.Equal(refusalResponse, result);
        await fileWriteTool.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnhanceResponseAsync_MultipleIdentityFiles_CreatesAllFiles()
    {
        var toolRegistry = Substitute.For<IToolRegistry>();
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        
        var fileWriteTool = Substitute.For<ITool>();
        fileWriteTool.Name.Returns("file_write");
        fileWriteTool.ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ToolResult { ToolName = "file_write", Success = true });
        
        var toolDict = new Dictionary<string, ITool> { { "file_write", fileWriteTool } };
        toolRegistry.Tools.Returns(toolDict);
        
        actionAuthorizer.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult { IsAuthorized = true, ActionType = "WriteUserMd" });
        
        var enhancer = new RefusalInterceptorResponseEnhancer(toolRegistry, actionAuthorizer, NullLogger<RefusalInterceptorResponseEnhancer>.Instance);
        
        var userMessage = "You have my permission to create and update the files.";
        var refusalResponse = """
            I am unable to directly create files on your local system.
            
            ### USER.md
            # User Profile
            
            ### SELF.md
            # SELF.md
            
            ### AGENTS.md
            # AGENTS.md
            """;
        
        var context = new ConversationContext
        {
            SystemPrompt = "test",
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };
        
        var result = await enhancer.EnhanceResponseAsync(userMessage, refusalResponse, context, CancellationToken.None);
        
        Assert.Contains("✓ Created", result);
        await fileWriteTool.Received(3).ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
