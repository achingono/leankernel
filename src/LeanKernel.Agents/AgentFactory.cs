using System.Collections.Concurrent;
using System.ClientModel;
using LeanKernel.Abstractions.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace LeanKernel.Agents;

/// <summary>
/// Factory for creating MAF-compatible chat clients backed by LiteLLM.
/// </summary>
public sealed class AgentFactory
{
    private readonly ConcurrentDictionary<string, IChatClient> _chatClients;
    private readonly LiteLlmConfig _config;
    private readonly ILogger<AgentFactory> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly OpenAIClient? _openAiClient;

    public AgentFactory(IOptions<LeanKernelConfig> config, ILogger<AgentFactory> logger, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config.Value.LiteLlm;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _openAiClient = new OpenAIClient(
            new ApiKeyCredential(_config.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(_config.BaseUrl) });
        _chatClients = new ConcurrentDictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase);
        _chatClients[_config.DefaultModel] = WrapWithFunctionInvocation(
            _openAiClient.GetChatClient(_config.DefaultModel).AsIChatClient());

        _logger.LogInformation(
            "AgentFactory initialized: model={Model}, endpoint={Endpoint}",
            _config.DefaultModel,
            _config.BaseUrl);
    }

    /// <summary>
    /// Constructor for testing with pre-built chat clients.
    /// </summary>
    public AgentFactory(
        IChatClient chatClient,
        ILogger<AgentFactory> logger,
        IReadOnlyDictionary<string, IChatClient>? chatClients = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _config = new LiteLlmConfig();
        _chatClients = new ConcurrentDictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase);
        _chatClients[_config.DefaultModel] = WrapWithFunctionInvocation(chatClient);

        if (chatClients is not null)
        {
            foreach (var pair in chatClients)
            {
                _chatClients[pair.Key] = WrapWithFunctionInvocation(pair.Value);
            }
        }
    }

    /// <summary>
    /// Gets the underlying chat client for direct invocation.
    /// </summary>
    public IChatClient ChatClient => GetChatClientForModel(_config.DefaultModel);

    /// <summary>
    /// Gets the configured default model name.
    /// </summary>
    public string DefaultModel => _config.DefaultModel;

    /// <summary>
    /// Gets or creates a chat client for the specified model.
    /// </summary>
    /// <param name="modelName">The model name to target.</param>
    /// <returns>The chat client configured for the requested model.</returns>
    public IChatClient GetChatClientForModel(string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        if (_chatClients.TryGetValue(modelName, out var chatClient))
        {
            return chatClient;
        }

        if (_openAiClient is null)
        {
            throw new InvalidOperationException($"No chat client is configured for model '{modelName}'.");
        }

        return _chatClients.GetOrAdd(modelName, name =>
        {
            _logger.LogDebug("Creating chat client for model {Model}", name);
            return WrapWithFunctionInvocation(_openAiClient.GetChatClient(name).AsIChatClient());
        });
    }

    private IChatClient WrapWithFunctionInvocation(IChatClient innerClient)
        => new FunctionInvokingChatClient(innerClient, _loggerFactory);
}
