using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public sealed class AttachmentTextExtractionServiceTests
{
    [Theory]
    [InlineData("image/jpeg", "screenshot.jpg")]
    [InlineData("image/png", "screenshot.png")]
    [InlineData("application/pdf", "report.pdf")]
    public void CanExtractText_AcceptsConfiguredAttachmentTypes(string contentType, string fileName)
    {
        var service = CreateService(new StaticJsonHandler("[]"));

        var canExtract = service.CanExtractText(contentType, fileName);

        Assert.True(canExtract);
    }

    [Fact]
    public void CanExtractText_UsesConfiguredSupportedTypes()
    {
        var service = CreateService(
            new StaticJsonHandler("[]"),
            new UnstructuredConfig
            {
                Enabled = true,
                BaseUrl = "http://unstructured:8000",
                TimeoutSeconds = 120,
                SupportedMimeTypes = ["application/x-custom-doc"],
                SupportedExtensions = [".custom"]
            });

        Assert.False(service.CanExtractText("image/jpeg", "screenshot.jpg"));
        Assert.True(service.CanExtractText("application/x-custom-doc", "file.bin"));
        Assert.True(service.CanExtractText("application/octet-stream", "file.custom"));
    }

    [Fact]
    public async Task ExtractTextAsync_ImageAttachment_UsesUnstructuredResponse()
    {
        var handler = new StaticJsonHandler("""
            [{"text":"Career\nActive Projects"}]
            """);
        var service = CreateService(handler);

        var text = await service.ExtractTextAsync(
            "image/jpeg",
            "Screenshot.jpg",
            [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10],
            CancellationToken.None);

        Assert.Equal("Career\nActive Projects", text);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://unstructured:8000/general/v0/general", handler.LastRequest.RequestUri!.ToString());
    }

    private static AttachmentTextExtractionService CreateService(
        HttpMessageHandler handler,
        UnstructuredConfig? unstructured = null)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://unstructured:8000")
        };

        return new AttachmentTextExtractionService(
            client,
            Options.Create(new LeanKernelConfig
            {
                Unstructured = unstructured ?? new UnstructuredConfig
                {
                    Enabled = true,
                    BaseUrl = "http://unstructured:8000",
                    TimeoutSeconds = 120
                }
            }),
            NullLogger<AttachmentTextExtractionService>.Instance);
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}