using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Mcp;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Tools;
using LeanKernel.Logic.Tools.BuiltIn;
using LeanKernel.Logic.Tools.BuiltIn.Data;
using LeanKernel.Logic.Tools.BuiltIn.FileSystem;
using LeanKernel.Logic.Tools.BuiltIn.Internet;
using LeanKernel.Logic.Tools.Dynamic;
using LeanKernel.Logic.Tools.Memory;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class IServiceProviderExtensions
{
    /// <summary>
    /// Discovers and registers all tools into the registry.
    /// Called once at startup from the DI registration path.
    /// </summary>
    public static async Task RegisterToolsAsync(
        this IServiceProvider services,
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

        logger.LogInformation("Tool runtime starting. Registering built-in, memory, and dynamic tools.");

        // Built-in tools
        RegisterBuiltInTools(registry, settings, scopeFactory, logger);

        // Memory tools
        await RegisterMemoryToolsAsync(registry, services, scopeFactory, logger, ct)
            .ConfigureAwait(false);

        // MCP server tools (SDK-based discovery)
        await RegisterMcpToolsAsync(registry, services, logger, ct)
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

        if (settings.FileSystem.Enabled)
        {
            TryRegister(registry, FileReadTool.Create(scopeFactory), logger);
            TryRegister(registry, FileWriteTool.Create(scopeFactory), logger);
            TryRegister(registry, FileEditTool.Create(scopeFactory), logger);
            TryRegister(registry, FileStatTool.Create(scopeFactory), logger);
            TryRegister(registry, FileCopyTool.Create(scopeFactory), logger);
            TryRegister(registry, FileMoveTool.Create(scopeFactory), logger);
            TryRegister(registry, FileDeleteTool.Create(scopeFactory), logger);
            TryRegister(registry, FileTouchTool.Create(scopeFactory), logger);
            TryRegister(registry, FileChmodTool.Create(scopeFactory), logger);
            TryRegister(registry, DirectoryListTool.Create(scopeFactory), logger);
            TryRegister(registry, DirectoryCreateTool.Create(scopeFactory), logger);
            TryRegister(registry, ExtractTextTool.Create(scopeFactory), logger);
        }
        else
        {
            logger.LogInformation("FileSystem tools are disabled (Agents:Tools:FileSystem:Enabled=false).");
        }

        if (settings.Internet.Enabled)
        {
            TryRegister(registry, WebFetchTool.Create(scopeFactory), logger);
            TryRegister(registry, HttpRequestTool.Create(scopeFactory), logger);
        }
        else
        {
            logger.LogInformation("Internet tools are disabled (Agents:Tools:Internet:Enabled=false).");
        }

        if (settings.DatabaseQuery.Enabled)
        {
            TryRegister(registry, DatabaseQueryTool.Create(scopeFactory), logger);
            TryRegister(registry, JsonTransformTool.Create(scopeFactory), logger);
            TryRegister(registry, CsvXlsxReadWriteTool.Create(scopeFactory), logger);
        }
        else
        {
            logger.LogInformation("Database query tools are disabled (Agents:Tools:DatabaseQuery:Enabled=false).");
        }

        // Webwright removed — replaced by MCP server tools registered via RegisterMcpToolsAsync
    }

    private static async Task RegisterMcpToolsAsync(
        IToolRegistry registry,
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct)
    {
        var mcpProvider = services.GetService<IMcpToolProvider>();
        if (mcpProvider is null)
        {
            logger.LogInformation("MCP tool provider not registered. Skipping MCP tool discovery.");
            return;
        }

        IReadOnlyList<ToolDefinition> mcpTools;
        try
        {
            mcpTools = await mcpProvider.DiscoverToolsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP tool discovery failed. No MCP tools will be registered.");
            return;
        }

        foreach (var tool in mcpTools)
        {
            TryRegister(registry, tool, logger);
        }
    }

    private static async Task RegisterMemoryToolsAsync(
        IToolRegistry registry,
        IServiceProvider services,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        CancellationToken ct)
    {
        var memoryService = services.GetService<IMemoryService>();
        if (memoryService is null)
        {
            logger.LogInformation("Memory MCP client not registered — memory tools will not be registered.");
            return;
        }

        MemoryCapabilityResult capability;
        try
        {
            var capabilityCheck = services.GetRequiredService<IMemoryCapabilityCheck>();
            capability = await capabilityCheck.ProbeAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Memory capability pre-check failed unexpectedly. Memory tools will not be registered.");
            return;
        }

        if (capability.Status == MemoryCapabilityStatus.Misconfigured)
        {
            throw new InvalidOperationException(
                $"Memory configuration is invalid: {capability.Reason}");
        }

        if (capability.Status == MemoryCapabilityStatus.Unavailable)
        {
            logger.LogWarning("Memory unavailable: {Reason}. Memory tools will not be registered.", capability.Reason);
            return;
        }

        if (capability.CanSearch)
        {
            TryRegister(registry, MemorySearchTool.Create(scopeFactory), logger);
        }

        if (capability.CanRead)
        {
            TryRegister(registry, MemoryReadTool.Create(scopeFactory), logger);
        }

        if (capability.CanWrite)
        {
            TryRegister(registry, MemoryWriteTool.Create(scopeFactory), logger);
        }

        if (capability.Status == MemoryCapabilityStatus.Degraded)
        {
            logger.LogWarning("Memory degraded: {Reason}", capability.Reason);
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
                LoadSkillFile(registry, scopeFactory, parser, filePath, logger);
            }
        }
    }

    private static void LoadSkillFile(
        IToolRegistry registry,
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