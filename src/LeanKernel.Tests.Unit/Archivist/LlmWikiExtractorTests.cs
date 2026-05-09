using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using NSubstitute;

namespace LeanKernel.Tests.Unit.Archivist;

public sealed class LlmWikiExtractorTests
{
    [Fact]
    public async Task ExtractAndIngestAsync_UsesLiteLlmChatCompletions()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "[{\"dimension\":\"who\",\"subject\":\"Kem\",\"claims\":[\"Kem is the user's agent\"]}]"
                      }
                    }
                  ]
                }
                """)
        });
        var wiki = Substitute.For<IWikiStore>();
        var extractor = CreateExtractor(handler, wiki);

        await extractor.ExtractAndIngestAsync("Who are you?", "I'm Kem.", "conversation:test", CancellationToken.None);

        Assert.Equal("/v1/chat/completions", handler.Requests[0].RequestUri!.AbsolutePath);
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"model\":\"small\"", body);
        await wiki.Received(1).IngestFactsAsync(
            Arg.Is<IEnumerable<WikiEntry>>(entries => entries.Any(entry => entry.Subject == "Kem")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAndIngestAsync_FallsBackWhenVersionedEndpointMissing()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/chat/completions")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("missing")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "[{\"dimension\":\"what\",\"subject\":\"Career\",\"claims\":[\"Career list exists\"]}]"
                          }
                        }
                      ]
                    }
                    """)
            };
        });
        var wiki = Substitute.For<IWikiStore>();
        var extractor = CreateExtractor(handler, wiki);

        await extractor.ExtractAndIngestAsync(new string('u', 5000), "assistant", "conversation:test", CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/chat/completions", handler.Requests[1].RequestUri!.AbsolutePath);
        var fallbackBody = await handler.Requests[1].Content!.ReadAsStringAsync();
        Assert.Contains("...[truncated for extraction]", fallbackBody);
        await wiki.Received(1).IngestFactsAsync(
            Arg.Is<IEnumerable<WikiEntry>>(entries => entries.Any(entry => entry.Subject == "Career")),
            Arg.Any<CancellationToken>());
    }

    private static LlmWikiExtractor CreateExtractor(HttpMessageHandler handler, IWikiStore wiki)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://litellm:4000")
        };

        return new LlmWikiExtractor(
            client,
            wiki,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    BaseUrl = "http://litellm:4000",
                    ApiKey = "sk-test",
                    DefaultModel = "small"
                },
                Ollama = new OllamaConfig
                {
                    Temperature = 0.1
                }
            }),
            NullLogger<LlmWikiExtractor>.Instance);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Content is not null)
            {
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(body, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "application/json");
            }

            return clone;
        }
    }
}
