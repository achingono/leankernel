using System.Text;

using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Events;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Events;
using LeanKernel.Services.Gateway.Providers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

public sealed class AttachmentIngestionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NonMultipartRequest_PassesThrough()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            CreatePermit(),
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            Mock.Of<IEventCollector>(),
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_MultipartButNotFormContentType_PassesThrough()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-datax";
        context.Request.Body = new MemoryStream();

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            CreatePermit(),
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            Mock.Of<IEventCollector>(),
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_InvalidMultipartBody_PassesThroughOnReadFailure()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=abc123";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("not-a-valid-multipart-body"));

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            CreatePermit(),
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            Mock.Of<IEventCollector>(),
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_MultipartWithNoFiles_PassesThrough()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.Body = new MemoryStream();
        context.Features.Set<IFormFeature>(new FormFeature(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>())));

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            CreatePermit(),
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            Mock.Of<IEventCollector>(),
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_JsonEnvelopeWithDocumentAttachment_StagesFileEmitsEventAndInvokesNext()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"leankernel-attachment-tests-{Guid.NewGuid():N}");
        var permit = CreatePermit();
        var json = """
        {
          "channel_attachments": [
            {
              "contentType": "application/pdf",
              "fileName": "doc.pdf",
              "fileDataUrl": "data:application/pdf;base64,SGVsbG8="
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var eventCollector = new Mock<IEventCollector>();
        DocumentIngestionRequestedEvent? emitted = null;
        eventCollector.Setup(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()))
            .Callback<DocumentIngestionRequestedEvent>(e => emitted = e);

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        try
        {
            await middleware.InvokeAsync(
                context,
                permit,
                Options.Create(new FileSettings { RootPath = tempRoot }),
                eventCollector.Object,
                Mock.Of<IChannelMemoryPolicyResolver>(),
                NullLogger<AttachmentIngestionMiddleware>.Instance);

            invoked.Should().BeTrue();
            eventCollector.Verify(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()), Times.Once);
            emitted.Should().NotBeNull();
            emitted!.FileName.Should().Be("doc.pdf");
            File.Exists(emitted.StagedFilePath).Should().BeTrue();
            (await File.ReadAllTextAsync(emitted.StagedFilePath)).Should().Be("Hello");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvokeAsync_JsonEnvelopeWithImageAttachment_DoesNotEmitIngestionEvent()
    {
        var json = """
        {
          "channel_attachments": [
            {
              "contentType": "image/png",
              "fileName": "picture.png",
              "fileDataUrl": "data:image/png;base64,SGVsbG8="
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var eventCollector = new Mock<IEventCollector>();
        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            CreatePermit(),
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            eventCollector.Object,
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        invoked.Should().BeTrue();
        eventCollector.Verify(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_JsonEnvelopeWithMalformedDataUrl_DoesNotEmitEvent()
    {
        var json = """
        {
          "channel_attachments": [
            {
              "contentType": "application/pdf",
              "fileName": "bad.pdf",
              "fileDataUrl": "data:application/pdf;base64,not-valid-base64"
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var eventCollector = new Mock<IEventCollector>();
        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            CreatePermit(),
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            eventCollector.Object,
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        invoked.Should().BeTrue();
        eventCollector.Verify(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_JsonEnvelope_ChannelNotReadable_ReturnsForbiddenWithoutInvokingNext()
    {
        var permit = CreatePermit();
        var requestedChannel = Guid.NewGuid();
        var json = $$"""
        {
          "channelId": "{{requestedChannel}}",
          "channel_attachments": [
            {
              "contentType": "application/pdf",
              "fileName": "doc.pdf",
              "fileDataUrl": "data:application/pdf;base64,SGVsbG8="
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var policyResolver = new Mock<IChannelMemoryPolicyResolver>();
        policyResolver.Setup(r => r.ResolveAsync(permit.TenantId, permit.ChannelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelMemoryPolicyResolution
            {
                TenantId = permit.TenantId,
                ChannelId = permit.ChannelId,
                ReadableChannelIds = [Guid.NewGuid()],
                MutuallyVisibleChannelIds = [],
            });

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            permit,
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            Mock.Of<IEventCollector>(),
            policyResolver.Object,
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_JsonEnvelope_TenantScopeWithoutBadgeIdentity_ReturnsForbidden()
    {
        var json = """
        {
          "availabilityScope": "tenant",
          "channel_attachments": [
            {
              "contentType": "application/pdf",
              "fileName": "doc.pdf",
              "fileDataUrl": "data:application/pdf;base64,SGVsbG8="
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var middleware = new AttachmentIngestionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(
            context,
            CreatePermit(badgeId: Guid.Empty),
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            Mock.Of<IEventCollector>(),
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_ChannelNotReadable_ReturnsForbiddenWithoutInvokingNext()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"leankernel-attachment-tests-{Guid.NewGuid():N}");
        var permit = CreatePermit();
        var requestedChannel = Guid.NewGuid();
        var context = CreateMultipartContext(
            file: CreateFormFile("payload", "doc.txt", "text/plain"),
            formValues: new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["channel_id"] = requestedChannel.ToString(),
            });

        var policyResolver = new Mock<IChannelMemoryPolicyResolver>();
        policyResolver.Setup(r => r.ResolveAsync(permit.TenantId, permit.ChannelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelMemoryPolicyResolution
            {
                TenantId = permit.TenantId,
                ChannelId = permit.ChannelId,
                ReadableChannelIds = [Guid.NewGuid()],
                MutuallyVisibleChannelIds = [],
            });

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        try
        {
            await middleware.InvokeAsync(
                context,
                permit,
                Options.Create(new FileSettings { RootPath = tempRoot }),
                Mock.Of<IEventCollector>(),
                policyResolver.Object,
                NullLogger<AttachmentIngestionMiddleware>.Instance);

            context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            invoked.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvokeAsync_ValidMultipart_StagesFileEmitsEventAndInvokesNext()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"leankernel-attachment-tests-{Guid.NewGuid():N}");
        var permit = CreatePermit();
        var context = CreateMultipartContext(
            file: CreateFormFile("payload", "doc.txt", "text/plain"),
            formValues: new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["channel_id"] = permit.ChannelId.ToString(),
                ["availability_scope"] = "channel",
            });

        var eventCollector = new Mock<IEventCollector>();
        DocumentIngestionRequestedEvent? emitted = null;
        eventCollector.Setup(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()))
            .Callback<DocumentIngestionRequestedEvent>(e => emitted = e);

        var policyResolver = new Mock<IChannelMemoryPolicyResolver>();
        policyResolver.Setup(r => r.ResolveAsync(permit.TenantId, permit.ChannelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelMemoryPolicyResolution
            {
                TenantId = permit.TenantId,
                ChannelId = permit.ChannelId,
                ReadableChannelIds = [permit.ChannelId],
                MutuallyVisibleChannelIds = [],
            });

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        try
        {
            await middleware.InvokeAsync(
                context,
                permit,
                Options.Create(new FileSettings { RootPath = tempRoot }),
                eventCollector.Object,
                policyResolver.Object,
                NullLogger<AttachmentIngestionMiddleware>.Instance);

            invoked.Should().BeTrue();
            eventCollector.Verify(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()), Times.Once);
            emitted.Should().NotBeNull();
            emitted!.AvailabilityScope.Should().Be(DocumentAvailabilityScope.Channel);
            emitted.ChannelId.Should().Be(permit.ChannelId);
            File.Exists(emitted.StagedFilePath).Should().BeTrue();
            (await File.ReadAllTextAsync(emitted.StagedFilePath)).Should().Be("payload");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvokeAsync_TenantScopeWithoutBadgeIdentity_ReturnsForbidden()
    {
        var permit = CreatePermit(badgeId: Guid.Empty);
        var context = CreateMultipartContext(
            file: CreateFormFile("payload", "doc.txt", "text/plain"),
            formValues: new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["availability_scope"] = "tenant",
            });

        var middleware = new AttachmentIngestionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(
            context,
            permit,
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            Mock.Of<IEventCollector>(),
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_ChatCompletionsWithImageUrlDataUrlPart_StagesFileEmitsEventAndInvokesNext()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"leankernel-attachment-tests-{Guid.NewGuid():N}");
        var permit = CreatePermit();
        var json = """
        {
          "model": "medium",
          "messages": [
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "Here's the talk track. Commit it to memory." },
                { "type": "image_url", "image_url": { "url": "data:application/pdf;base64,SGVsbG8=" } }
              ]
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var eventCollector = new Mock<IEventCollector>();
        DocumentIngestionRequestedEvent? emitted = null;
        eventCollector.Setup(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()))
            .Callback<DocumentIngestionRequestedEvent>(e => emitted = e);

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        try
        {
            await middleware.InvokeAsync(
                context,
                permit,
                Options.Create(new FileSettings { RootPath = tempRoot }),
                eventCollector.Object,
                Mock.Of<IChannelMemoryPolicyResolver>(),
                NullLogger<AttachmentIngestionMiddleware>.Instance);

            invoked.Should().BeTrue();
            eventCollector.Verify(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()), Times.Once);
            emitted.Should().NotBeNull();
            emitted!.FileName.Should().NotBeNullOrWhiteSpace();
            File.Exists(emitted.StagedFilePath).Should().BeTrue();
            (await File.ReadAllTextAsync(emitted.StagedFilePath)).Should().Be("Hello");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvokeAsync_ChatCompletionsWithFilePart_StagesFileEmitsEventAndInvokesNext()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"leankernel-attachment-tests-{Guid.NewGuid():N}");
        var permit = CreatePermit();
        var json = """
        {
          "model": "medium",
          "messages": [
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "Commit this to memory." },
                {
                  "type": "file",
                  "file": {
                    "filename": "talk-track.md",
                    "file_data": "data:text/markdown;base64,IyBUYWxrIHRyYWNr"
                  }
                }
              ]
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var eventCollector = new Mock<IEventCollector>();
        DocumentIngestionRequestedEvent? emitted = null;
        eventCollector.Setup(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()))
            .Callback<DocumentIngestionRequestedEvent>(e => emitted = e);

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        try
        {
            await middleware.InvokeAsync(
                context,
                permit,
                Options.Create(new FileSettings { RootPath = tempRoot }),
                eventCollector.Object,
                Mock.Of<IChannelMemoryPolicyResolver>(),
                NullLogger<AttachmentIngestionMiddleware>.Instance);

            invoked.Should().BeTrue();
            eventCollector.Verify(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()), Times.Once);
            emitted.Should().NotBeNull();
            emitted!.FileName.Should().Be("talk-track.md");
            File.Exists(emitted.StagedFilePath).Should().BeTrue();
            (await File.ReadAllTextAsync(emitted.StagedFilePath)).Should().Be("# Talk track");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvokeAsync_ResponsesApiInputFileDataPart_StagesFileEmitsEventAndInvokesNext()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"leankernel-attachment-tests-{Guid.NewGuid():N}");
        var permit = CreatePermit();
        var json = """
        {
          "model": "medium",
          "input": [
            {
              "role": "user",
              "content": [
                { "type": "input_text", "text": "Commit this to memory." },
                {
                  "type": "input_file",
                  "file_data": "data:text/plain;base64,VGFsayB0cmFjaw==",
                  "filename": "notes.txt"
                }
              ]
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var eventCollector = new Mock<IEventCollector>();
        DocumentIngestionRequestedEvent? emitted = null;
        eventCollector.Setup(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()))
            .Callback<DocumentIngestionRequestedEvent>(e => emitted = e);

        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        try
        {
            await middleware.InvokeAsync(
                context,
                permit,
                Options.Create(new FileSettings { RootPath = tempRoot }),
                eventCollector.Object,
                Mock.Of<IChannelMemoryPolicyResolver>(),
                NullLogger<AttachmentIngestionMiddleware>.Instance);

            invoked.Should().BeTrue();
            eventCollector.Verify(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()), Times.Once);
            emitted.Should().NotBeNull();
            emitted!.FileName.Should().Be("notes.txt");
            File.Exists(emitted.StagedFilePath).Should().BeTrue();
            (await File.ReadAllTextAsync(emitted.StagedFilePath)).Should().Be("Talk track");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvokeAsync_OpenAiRequestWithoutDataUrlParts_PassesThroughWithoutEvent()
    {
        var json = """
        {
          "model": "medium",
          "messages": [
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "Just a text message, no attachment." }
              ]
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var eventCollector = new Mock<IEventCollector>();
        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            CreatePermit(),
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            eventCollector.Object,
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        invoked.Should().BeTrue();
        eventCollector.Verify(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_OpenAiImageUrlPart_DoesNotEmitIngestionEvent()
    {
        var json = """
        {
          "model": "medium",
          "messages": [
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "What is in this image?" },
                { "type": "image_url", "image_url": { "url": "data:image/png;base64,SGVsbG8=" } }
              ]
            }
          ]
        }
        """;

        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var eventCollector = new Mock<IEventCollector>();
        var invoked = false;
        var middleware = new AttachmentIngestionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            CreatePermit(),
            Options.Create(new FileSettings { RootPath = Path.GetTempPath() }),
            eventCollector.Object,
            Mock.Of<IChannelMemoryPolicyResolver>(),
            NullLogger<AttachmentIngestionMiddleware>.Instance);

        invoked.Should().BeTrue();
        eventCollector.Verify(c => c.Emit(It.IsAny<DocumentIngestionRequestedEvent>()), Times.Never);
    }

    private static DefaultHttpContext CreateMultipartContext(
        IFormFile file,
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues>? formValues = null)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.Body = new MemoryStream();

        var files = new FormFileCollection { file };
        var values = formValues ?? new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();
        var form = new FormCollection(values, files);
        context.Features.Set<IFormFeature>(new FormFeature(form));

        return context;
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
            IsAuthenticated = badgeId?.Equals(Guid.Empty) != true,
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