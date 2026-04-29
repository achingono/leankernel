using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using LeanKernel.Core.Configuration;
using System.ClientModel;

namespace LeanKernel.Thinker;

/// <summary>
/// Factory for building MAF agents backed by LiteLLM via the OpenAI SDK.
/// Replaces <c>KernelFactory</c> as the single point where the LLM client
/// is configured and agents are created.
/// </summary>
public sealed class AgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<AgentFactory> _logger;

    public AgentFactory(IOptions<LeanKernelConfig> config, ILogger<AgentFactory> logger)
    {
        _logger = logger;
        var cfg = config.Value.LiteLlm;

        // LiteLLM is OpenAI-compatible — use the OpenAI SDK with a custom endpoint
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(cfg.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(cfg.BaseUrl) });

        _chatClient = openAiClient
            .GetChatClient(cfg.DefaultModel)
            .AsIChatClient();

        _logger.LogInformation(
            "AgentFactory initialized: model={Model}, endpoint={Endpoint}",
            cfg.DefaultModel, cfg.BaseUrl);
    }

    /// <summary>
    /// Constructor accepting a pre-built <see cref="IChatClient"/> (for testing).
    /// </summary>
    internal AgentFactory(IChatClient chatClient, ILogger<AgentFactory> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <summary>
    /// The underlying chat client (exposed for middleware wrapping in M4).
    /// </summary>
    public IChatClient ChatClient => _chatClient;

    /// <summary>
    /// Create a <see cref="ChatClientAgent"/> with the given instructions and optional tools.
    /// </summary>
    public ChatClientAgent CreateAgent(
        string instructions,
        IList<AITool>? tools = null)
    {
        _logger.LogDebug("Creating agent: instructions={Length} chars, tools={ToolCount}",
            instructions.Length, tools?.Count ?? 0);

        return new ChatClientAgent(_chatClient,
            instructions: instructions,
            tools: tools);
    }
}
