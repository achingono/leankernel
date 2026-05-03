using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using LeanKernel.Core.Configuration;
using LeanKernel.Thinker.Middleware;
using System.ClientModel;

namespace LeanKernel.Thinker;

/// <summary>
/// Factory for building MAF agents backed by LiteLLM via the OpenAI SDK.
/// Applies middleware pipeline: function logging → chat client.
/// Diagnostics middleware is applied at agent level by callers.
/// </summary>
public sealed class AgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly OpenAIClient? _openAiClient;
    private readonly FunctionLoggingMiddleware? _functionLogging;
    private readonly DiagnosticsMiddleware? _diagnostics;
    private readonly ILogger<AgentFactory> _logger;

    public AgentFactory(
        IOptions<LeanKernelConfig> config,
        ILogger<AgentFactory> logger,
        FunctionLoggingMiddleware? functionLogging = null,
        DiagnosticsMiddleware? diagnostics = null)
    {
        _logger = logger;
        _functionLogging = functionLogging;
        _diagnostics = diagnostics;
        var cfg = config.Value.LiteLlm;

        // LiteLLM is OpenAI-compatible — use the OpenAI SDK with a custom endpoint
        _openAiClient = new OpenAIClient(
            new ApiKeyCredential(cfg.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(cfg.BaseUrl) });

        IChatClient client = _openAiClient
            .GetChatClient(cfg.DefaultModel)
            .AsIChatClient();

        // Apply chat-client-level middleware
        if (functionLogging is not null)
            client = functionLogging.Wrap(client);

        _chatClient = client;

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
    /// The underlying chat client.
    /// </summary>
    public IChatClient ChatClient => _chatClient;

    /// <summary>
    /// Create a <see cref="ChatClientAgent"/> with the given instructions and optional tools.
    /// Wraps with diagnostics middleware when available.
    /// </summary>
    public ChatClientAgent CreateAgent(
        string instructions,
        IReadOnlyList<AITool>? tools = null)
    {
        _logger.LogDebug("Creating agent: instructions={Length} chars, tools={ToolCount}",
            instructions.Length, tools?.Count ?? 0);

        var agent = new ChatClientAgent(_chatClient,
            instructions: instructions,
            tools: tools?.ToList());

        return agent;
    }

    /// <summary>
    /// Create an agent targeting a specific LiteLLM model alias (e.g. "small", "medium", "large").
    /// Used by the intelligent routing pipeline to invoke a particular tier (FR-3).
    /// Falls back to the default client when the <see cref="OpenAIClient"/> is unavailable (test mode).
    /// </summary>
    public ChatClientAgent CreateAgentForModel(
        string modelAlias,
        string instructions,
        IReadOnlyList<AITool>? tools = null)
    {
        _logger.LogDebug("Creating agent for model alias '{Alias}': instructions={Length} chars, tools={ToolCount}",
            modelAlias, instructions.Length, tools?.Count ?? 0);

        // Build a new IChatClient scoped to this model alias.
        IChatClient client;
        if (_openAiClient is not null)
        {
            client = _openAiClient.GetChatClient(modelAlias).AsIChatClient();
            if (_functionLogging is not null)
                client = _functionLogging.Wrap(client);
        }
        else
        {
            // Fallback: test mode — use the pre-built client regardless of alias.
            client = _chatClient;
        }

        return new ChatClientAgent(client,
            instructions: instructions,
            tools: tools?.ToList());
    }

    /// <summary>
    /// Create an agent wrapped with diagnostics middleware (for top-level calls
    /// where timing/stats should be captured).
    /// </summary>
    public AIAgent CreateInstrumentedAgent(
        string instructions,
        IReadOnlyList<AITool>? tools = null)
    {
        var agent = CreateAgent(instructions, tools);
        return _diagnostics?.Wrap(agent) ?? agent;
    }
}
