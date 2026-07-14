using LeanKernel.Gateway.Providers;
using LeanKernel.Gateway.Tools.BuiltIn;
using LeanKernel.Gateway.Tools.Dynamic;
using LeanKernel.Gateway.Tools.Wiki;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Tools;

/// <summary>
/// Orchestrates startup registration of built-in tools, wiki tools, and dynamic SKILL.md tools
/// into the shared <see cref="IToolRegistry"/>.
/// </summary>
public static class ToolRuntimeStartup
{
    /// <summary>
    /// Discovers and registers all tools into the registry.
    /// Called once at startup from the DI registration path.
    /// </summary>
    public static async Task RegisterToolsAsync(
        IServiceProvider services,
        CancellationToken ct = default)
    {
        var registry = services.GetRequiredService<IToolRegistry>();
        var settings = services.GetRequiredService<IOptions<AgentSettings>>().Value.Tools;
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("ToolRuntimeStartup");
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();

        if (!settings.Enabled)
        {
            logger.LogInformation("Tool runtime is disabled (Agents:Tools:Enabled=false). Skipping tool registration.");
            return;
        }

        logger.LogInformation("Tool runtime starting. Registering built-in, wiki, and dynamic tools.");

        // Built-in tools
        RegisterBuiltInTools(registry, settings, scopeFactory, logger);

        // GBrain wiki tools
        await RegisterWikiToolsAsync(registry, settings, services, scopeFactory, logger, ct)
            .ConfigureAwait(false);

        // Dynamic SKILL.md tools
        RegisterDynamicTools(registry, settings, scopeFactory, logger);

        logger.LogInformation("Tool runtime ready. {Count} tool(s) registered: {Names}",
            registry.Tools.Count,
            string.Join(", ", registry.Tools.Select(t => t.Name)));
    }

    private static void RegisterBuiltInTools(
        IToolRegistry registry,
        ToolSettings settings,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        TryRegister(registry, WebSearchTool.Create(scopeFactory), logger);
        TryRegister(registry, FileSearchTool.Create(scopeFactory), logger);

        if (settings.BuiltIns.Calculation.Enabled)
        {
            foreach (var tool in CalculationTools.Create(scopeFactory))
            {
                TryRegister(registry, tool, logger);
            }
        }
        else
        {
            logger.LogInformation("Calculation/aggregation helpers are disabled (Agents:Tools:BuiltIns:Calculation:Enabled=false).");
        }
    }

    private static async Task RegisterWikiToolsAsync(
        IToolRegistry registry,
        ToolSettings settings,
        IServiceProvider services,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        CancellationToken ct)
    {
        var gbrain = services.GetService<IGBrainMcpClient>();
        if (gbrain is null)
        {
            logger.LogInformation("GBrain MCP client not registered — wiki tools will not be registered.");
            return;
        }

        GBrainCapabilityResult capability;
        try
        {
            var capabilityCheck = services.GetRequiredService<GBrainCapabilityCheck>();
            capability = await capabilityCheck.ProbeAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GBrain capability pre-check failed unexpectedly. Wiki tools will not be registered.");
            return;
        }

        if (capability.Status == GBrainCapabilityStatus.Misconfigured)
        {
            throw new InvalidOperationException(
                $"GBrain configuration is invalid: {capability.Reason}");
        }

        if (capability.Status == GBrainCapabilityStatus.Unavailable)
        {
            logger.LogWarning("GBrain unavailable: {Reason}. Wiki tools will not be registered.", capability.Reason);
            return;
        }

        if (capability.CanSearch)
        {
            TryRegister(registry, WikiSearchTool.Create(scopeFactory), logger);
        }

        if (capability.CanRead)
        {
            TryRegister(registry, WikiReadTool.Create(scopeFactory), logger);
        }

        if (capability.CanWrite)
        {
            TryRegister(registry, WikiWriteTool.Create(scopeFactory), logger);
        }

        if (capability.Status == GBrainCapabilityStatus.Degraded)
        {
            logger.LogWarning("GBrain degraded: {Reason}", capability.Reason);
        }
    }

    private static void RegisterDynamicTools(
        IToolRegistry registry,
        ToolSettings settings,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        var parser = new SkillParser();

        foreach (var basePath in settings.SkillBasePaths)
        {
            if (!Directory.Exists(basePath))
            {
                logger.LogDebug("Skill base path does not exist: {Path}. Skipping.", basePath);
                continue;
            }

            var files = Directory.EnumerateFiles(basePath, "SKILL.md", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(basePath, "*.skill.md", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in files)
            {
                LoadSkillFile(registry, settings, scopeFactory, parser, filePath, logger);
            }
        }
    }

    private static void LoadSkillFile(
        IToolRegistry registry,
        ToolSettings settings,
        IServiceScopeFactory scopeFactory,
        SkillParser parser,
        string filePath,
        ILogger logger)
    {
        SkillDefinition? skill;
        try
        {
            skill = parser.Parse(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse SKILL.md at {Path}. Skipping.", filePath);
            return;
        }

        if (skill is null)
        {
            logger.LogWarning("SKILL.md at {Path} is invalid or has no valid operations. Skipping.", filePath);
            return;
        }

        // Validate egress configuration
        foreach (var op in skill.Operations)
        {
            if (string.IsNullOrWhiteSpace(skill.Runtime.BaseUrl))
            {
                logger.LogWarning("Skill '{Name}' operation '{Op}' has no baseUrl. Skipping skill.", skill.Name, op.Id);
                return;
            }

            var urlCheck = $"{skill.Runtime.BaseUrl.TrimEnd('/')}{op.HttpPath}";
            var host = ExtractHost(urlCheck);
            if (host is not null && EgressValidator.IsPrivateOrLoopbackHost(host) &&
                !skill.AllowedHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning(
                    "Skill '{Name}' operation '{Op}' targets loopback/private host '{Host}'. Skipping skill.",
                    skill.Name, op.Id, host);
                return;
            }
        }

        // auth.secretRef validation
        if (skill.Runtime.Auth.Type == "bearer" &&
            string.IsNullOrWhiteSpace(skill.Runtime.Auth.SecretRef))
        {
            logger.LogWarning("Skill '{Name}' uses bearer auth but has no secretRef. Skipping.", skill.Name);
            return;
        }

        foreach (var op in skill.Operations)
        {
            var toolName = $"{skill.Name}_{op.Id}";
            var tool = DynamicSkillTool.Create(skill, op, scopeFactory);
            if (!registry.TryRegister(tool))
            {
                logger.LogWarning(
                    "Duplicate tool name '{Name}' from skill '{Skill}' operation '{Op}'. Skipping.",
                    toolName, skill.Name, op.Id);
            }
            else
            {
                logger.LogInformation("Dynamic tool '{Name}' registered from {Path}.", toolName, filePath);
            }
        }
    }

    private static void TryRegister(IToolRegistry registry, ToolDefinition tool, ILogger logger)
    {
        if (!registry.TryRegister(tool))
        {
            logger.LogWarning("Tool '{Name}' already registered. Skipping duplicate.", tool.Name);
        }
        else
        {
            logger.LogDebug("Built-in tool '{Name}' registered.", tool.Name);
        }
    }

    private static string? ExtractHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Host;
    }
}
