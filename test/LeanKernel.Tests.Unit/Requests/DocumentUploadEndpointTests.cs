using System.Reflection;
using System.Text;

using FluentAssertions;

using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Gateway.Requests;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Requests;

public sealed class DocumentUploadEndpointTests
{
    private static readonly MethodInfo HandleUploadAsyncMethod =
        typeof(DocumentUploadEndpoint).GetMethod("HandleUploadAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("DocumentUploadEndpoint.HandleUploadAsync was not found.");

    [Fact]
    public async Task HandleUploadAsync_EmptyFile_ReturnsBadRequest()
    {
        var permit = CreatePermit();
        var queue = new Mock<IDocumentIngestionQueue>();
        var context = CreateHttpContext(policyReadableChannels: [permit.ChannelId]);
        var file = CreateFormFile(string.Empty, "empty.txt", "text/plain");

        var result = await InvokeHandleUploadAsync(context, file, Guid.NewGuid().ToString(), null, permit, queue.Object);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>();
        result.As<IStatusCodeHttpResult>().StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task HandleUploadAsync_MissingChannelId_ReturnsBadRequest()
    {
        var permit = CreatePermit();
        var queue = new Mock<IDocumentIngestionQueue>();
        var context = CreateHttpContext(policyReadableChannels: [permit.ChannelId]);
        var file = CreateFormFile("hello", "doc.txt", "text/plain");

        var result = await InvokeHandleUploadAsync(context, file, string.Empty, null, permit, queue.Object);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>();
        result.As<IStatusCodeHttpResult>().StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task HandleUploadAsync_InvalidChannelId_ReturnsBadRequest()
    {
        var permit = CreatePermit();
        var queue = new Mock<IDocumentIngestionQueue>();
        var context = CreateHttpContext(policyReadableChannels: [permit.ChannelId]);
        var file = CreateFormFile("hello", "doc.txt", "text/plain");

        var result = await InvokeHandleUploadAsync(context, file, "not-a-guid", null, permit, queue.Object);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>();
        result.As<IStatusCodeHttpResult>().StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task HandleUploadAsync_ChannelNotReadable_ReturnsForbid()
    {
        var permit = CreatePermit();
        var queue = new Mock<IDocumentIngestionQueue>();
        var requestedChannel = Guid.NewGuid();
        var context = CreateHttpContext(policyReadableChannels: [Guid.NewGuid()]);
        var file = CreateFormFile("hello", "doc.txt", "text/plain");

        var result = await InvokeHandleUploadAsync(context, file, requestedChannel.ToString(), null, permit, queue.Object);

        result.Should().BeAssignableTo<ForbidHttpResult>();
        queue.Verify(
            q => q.EnqueueAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleUploadAsync_TenantScopeWithoutBadgeIdentity_ReturnsForbid()
    {
        var permit = CreatePermit(badgeId: Guid.Empty);
        var queue = new Mock<IDocumentIngestionQueue>();
        var context = CreateHttpContext(policyReadableChannels: [permit.ChannelId]);
        var file = CreateFormFile("hello", "doc.txt", "text/plain");

        var result = await InvokeHandleUploadAsync(context, file, permit.ChannelId.ToString(), "tenant", permit, queue.Object);

        result.Should().BeAssignableTo<ForbidHttpResult>();
        queue.Verify(
            q => q.EnqueueAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleUploadAsync_ValidUpload_StagesFileAndEnqueuesJob()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"leankernel-upload-tests-{Guid.NewGuid():N}");
        var permit = CreatePermit();
        var queue = new Mock<IDocumentIngestionQueue>();
        DocumentIngestionJob? queuedJob = null;
        queue.Setup(q => q.EnqueueAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentIngestionJob, CancellationToken>((job, _) => queuedJob = job)
            .Returns(Task.CompletedTask);

        var context = CreateHttpContext(policyReadableChannels: [permit.ChannelId], rootPath: rootPath);
        var file = CreateFormFile("hello world", "notes.txt", "text/plain");

        try
        {
            var result = await InvokeHandleUploadAsync(
                context,
                file,
                permit.ChannelId.ToString(),
                "unknown-scope-value",
                permit,
                queue.Object);

            result.Should().BeAssignableTo<IStatusCodeHttpResult>();
            result.As<IStatusCodeHttpResult>().StatusCode.Should().Be(StatusCodes.Status202Accepted);
            queue.Verify(
                q => q.EnqueueAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()),
                Times.Once);

            queuedJob.Should().NotBeNull();
            queuedJob!.AvailabilityScope.Should().Be(DocumentAvailabilityScope.User);
            queuedJob.Source.Should().Be(DocumentIngestionSource.Upload);
            File.Exists(queuedJob.FilePath).Should().BeTrue();
            (await File.ReadAllTextAsync(queuedJob.FilePath)).Should().Be("hello world");
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static async Task<IResult> InvokeHandleUploadAsync(
        HttpContext context,
        IFormFile file,
        string channelId,
        string? availabilityScope,
        IPermit permit,
        IDocumentIngestionQueue queue)
    {
        var task = (Task<IResult>)HandleUploadAsyncMethod.Invoke(null, [context, file, channelId, availabilityScope, permit, queue])!;
        return await task;
    }

    private static DefaultHttpContext CreateHttpContext(IReadOnlyCollection<Guid> policyReadableChannels, string? rootPath = null)
    {
        var tenantId = Guid.NewGuid();
        var permitChannel = policyReadableChannels.FirstOrDefault();
        var policy = new ChannelMemoryPolicyResolution
        {
            TenantId = tenantId,
            ChannelId = permitChannel,
            ReadableChannelIds = policyReadableChannels,
            MutuallyVisibleChannelIds = [],
        };

        var policyResolver = new Mock<IChannelMemoryPolicyResolver>();
        policyResolver.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var services = new ServiceCollection()
            .AddSingleton(policyResolver.Object)
            .AddSingleton<IOptions<FileSettings>>(Options.Create(new FileSettings
            {
                RootPath = rootPath ?? Path.Combine(Path.GetTempPath(), $"leankernel-upload-tests-{Guid.NewGuid():N}"),
            }))
            .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices = services,
        };
    }

    private static IFormFile CreateFormFile(string content, string fileName, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private static IPermit CreatePermit(Guid? badgeId = null)
        => new StubPermit
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            Badge = new Badge
            {
                Id = badgeId ?? Guid.NewGuid(),
                Email = "user@test.local",
                FullName = "Test User",
            },
        };

    private sealed class StubPermit : IPermit
    {
        public Guid PersonId { get; init; }

        public Guid UserId { get; init; }

        public Guid TenantId { get; init; }

        public Guid ChannelId { get; init; }

        public string HostName { get; init; } = "localhost";

        public bool IsAuthenticated { get; init; } = true;

        public string? SessionId { get; init; }

        public Badge Badge { get; init; } = new();
    }
}
