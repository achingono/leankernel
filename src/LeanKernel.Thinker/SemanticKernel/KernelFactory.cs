using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Thinker.SemanticKernel;

/// <summary>
/// Factory for building Semantic Kernel instances configured to
/// communicate with LLM providers through the LiteLLM proxy.
/// </summary>
public sealed class KernelFactory
{
    private readonly LeanKernelConfig _config;
    private readonly ILogger<KernelFactory> _logger;

    public KernelFactory(IOptions<LeanKernelConfig> config, ILogger<KernelFactory> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// Build a Kernel instance pointing to LiteLLM as an OpenAI-compatible endpoint.
    /// LiteLLM routes to the appropriate provider (OpenAI, Anthropic, Ollama, etc).
    /// </summary>
    public Kernel Build()
    {
        var builder = Kernel.CreateBuilder();

        builder.AddOpenAIChatCompletion(
            modelId: _config.LiteLlm.DefaultModel,
            apiKey: _config.LiteLlm.ApiKey,
            endpoint: new Uri(_config.LiteLlm.BaseUrl));

        _logger.LogInformation(
            "Semantic Kernel initialized: model={Model}, endpoint={Endpoint}",
            _config.LiteLlm.DefaultModel, _config.LiteLlm.BaseUrl);

        return builder.Build();
    }
}
