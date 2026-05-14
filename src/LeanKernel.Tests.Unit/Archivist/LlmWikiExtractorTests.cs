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
                        "content": "{\"facts\":[{\"who\":\"Kem\",\"what\":\"Role\",\"claim\":\"Kem is the user's agent\",\"subject\":\"Kem\",\"primaryDimension\":\"who\",\"sourceQuote\":\"I'm Kem.\",\"aliases\":[\"KEM\"],\"tags\":[\"identity\"]}]}"
                      }
                    }
                  ]
                }
                """)
        });
        var wiki = Substitute.For<IWikiStore>();
        var extractor = CreateExtractor(handler, wiki, new WikiFactMapper());

        await extractor.ExtractAndIngestAsync("Who are you?", "I'm Kem.", "conversation:test", CancellationToken.None);

        Assert.Equal("/v1/chat/completions", handler.Requests[0].RequestUri!.AbsolutePath);
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"model\":\"small\"", body);
        Assert.Contains("\"response_format\":{\"type\":\"json_object\"}", body);
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
                            "content": "{\"facts\":[{\"what\":\"Career\",\"claim\":\"Career list exists in notes\",\"subject\":\"Career\",\"primaryDimension\":\"what\",\"sourceQuote\":\"career list exists\"}]}"
                          }
                        }
                      ]
                    }
                    """)
            };
        });
        var wiki = Substitute.For<IWikiStore>();
        var extractor = CreateExtractor(handler, wiki, new WikiFactMapper());

        await extractor.ExtractAndIngestAsync(new string('u', 5000), "assistant", "conversation:test", CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/chat/completions", handler.Requests[1].RequestUri!.AbsolutePath);
        var fallbackBody = await handler.Requests[1].Content!.ReadAsStringAsync();
        Assert.Contains("...[truncated for extraction]", fallbackBody);
        await wiki.Received(1).IngestFactsAsync(
            Arg.Is<IEnumerable<WikiEntry>>(entries => entries.Any(entry => entry.Subject == "Career")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_RejectsMalformedOrInvalidFacts()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "{\"facts\":[{\"subject\":\"Unknown\",\"claim\":\"ok\",\"primaryDimension\":\"who\"}]}"
                      }
                    }
                  ]
                }
                """)
        });
        var wiki = Substitute.For<IWikiStore>();
        var extractor = CreateExtractor(handler, wiki, new WikiFactMapper());

        var facts = await extractor.ExtractAsync("hello", "assistant", "conversation:test", CancellationToken.None);

        Assert.Empty(facts);
    }

    [Fact]
    public async Task ExtractAsync_EmptyModelContent_ReturnsEmpty()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": ""
                      }
                    }
                  ]
                }
                """)
        });
        var wiki = Substitute.For<IWikiStore>();
        var extractor = CreateExtractor(handler, wiki, new WikiFactMapper());

        var facts = await extractor.ExtractAsync("user", "assistant", "conversation:test", CancellationToken.None);

        Assert.Empty(facts);
    }

    [Fact]
    public void ParseExtractedFacts_IgnoresFencedEnvelope()
    {
        const string response = """
            ```json
            {"facts":[{"who":"Kem","claim":"Kem is an agent","subject":"Kem","primaryDimension":"who","sourceQuote":"I am Kem."}]}
            ```
            """;

        var facts = LlmWikiExtractor.ParseExtractedFacts(response, "conversation:test", NullLogger<LlmWikiExtractor>.Instance);

        Assert.Single(facts);
        Assert.Equal("Kem", facts[0].Subject);
    }

    [Fact]
    public void ParseExtractedFacts_MalformedJson_ReturnsEmpty()
    {
        var facts = LlmWikiExtractor.ParseExtractedFacts("{\"facts\":[", "conversation:test", NullLogger<LlmWikiExtractor>.Instance);
        Assert.Empty(facts);
    }

    [Fact]
    public void ParseExtractedFacts_InvalidDimension_ReturnsEmpty()
    {
        var facts = LlmWikiExtractor.ParseExtractedFacts(
            "{\"facts\":[{\"who\":\"Kem\",\"claim\":\"Kem is an agent\",\"subject\":\"Kem\",\"primaryDimension\":\"invalid\"}]}",
            "conversation:test",
            NullLogger<LlmWikiExtractor>.Instance);
        Assert.Empty(facts);
    }

    private static LlmWikiExtractor CreateExtractor(HttpMessageHandler handler, IWikiStore wiki, WikiFactMapper mapper)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://litellm:4000")
        };

        return new LlmWikiExtractor(
            client,
            wiki,
            mapper,
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
