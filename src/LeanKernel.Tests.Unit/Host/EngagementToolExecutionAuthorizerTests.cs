using NSubstitute;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class EngagementToolExecutionAuthorizerTests
{
    [Fact]
    public async Task AuthorizeAsync_ProfileWrite_UsesSpecificWriteAction()
    {
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        actionAuthorizer.AuthorizeAsync("WriteSelfMd", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult
            {
                IsAuthorized = true,
                ActionType = "WriteSelfMd",
                Reason = "allowed"
            });

        var authorizer = new EngagementToolExecutionAuthorizer(actionAuthorizer);
        var result = await authorizer.AuthorizeAsync("file_write", """{"path":"SELF.md","content":"hi"}""", CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.Equal("WriteSelfMd", result.ActionType);
        await actionAuthorizer.Received(1).AuthorizeAsync("WriteSelfMd", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeAsync_ProfileWrite_WithAbsolutePath_UsesSpecificWriteAction()
    {
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        actionAuthorizer.AuthorizeAsync("WriteUserMd", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult
            {
                IsAuthorized = true,
                ActionType = "WriteUserMd",
                Reason = "allowed"
            });

        var authorizer = new EngagementToolExecutionAuthorizer(actionAuthorizer);
        var result = await authorizer.AuthorizeAsync(
            "file_write",
            """{"path":"/app/data/agents/main/USER.md","content":"hi"}""",
            CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.Equal("WriteUserMd", result.ActionType);
        await actionAuthorizer.Received(1).AuthorizeAsync("WriteUserMd", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeAsync_AgentsWrite_WithRelativePath_UsesSpecificWriteAction()
    {
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        actionAuthorizer.AuthorizeAsync("WriteAgentsMd", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult
            {
                IsAuthorized = true,
                ActionType = "WriteAgentsMd",
                Reason = "allowed"
            });

        var authorizer = new EngagementToolExecutionAuthorizer(actionAuthorizer);
        var result = await authorizer.AuthorizeAsync(
            "file_write",
            """{"path":"data/agents/main/AGENTS.md","content":"hi"}""",
            CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.Equal("WriteAgentsMd", result.ActionType);
        await actionAuthorizer.Received(1).AuthorizeAsync("WriteAgentsMd", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeAsync_DirectoryList_UsesListFilesAction()
    {
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        actionAuthorizer.AuthorizeAsync("ListFiles", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult
            {
                IsAuthorized = true,
                ActionType = "ListFiles",
                Reason = "allowed"
            });

        var authorizer = new EngagementToolExecutionAuthorizer(actionAuthorizer);
        var result = await authorizer.AuthorizeAsync("directory_list", "{}", CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.Equal("ListFiles", result.ActionType);
        await actionAuthorizer.Received(1).AuthorizeAsync("ListFiles", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeAsync_FileSearch_UsesSearchFilesAction()
    {
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        actionAuthorizer.AuthorizeAsync("SearchFiles", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult
            {
                IsAuthorized = true,
                ActionType = "SearchFiles",
                Reason = "allowed"
            });

        var authorizer = new EngagementToolExecutionAuthorizer(actionAuthorizer);
        var result = await authorizer.AuthorizeAsync("file_search", """{"query":"resume"}""", CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.Equal("SearchFiles", result.ActionType);
        await actionAuthorizer.Received(1).AuthorizeAsync("SearchFiles", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeAsync_UnknownTool_AllowsWithoutCallingActionAuthorizer()
    {
        var actionAuthorizer = Substitute.For<IActionAuthorizer>();
        var authorizer = new EngagementToolExecutionAuthorizer(actionAuthorizer);

        var result = await authorizer.AuthorizeAsync("unknown_tool", "{}", CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.Null(result.ActionType);
        await actionAuthorizer.DidNotReceive().AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
