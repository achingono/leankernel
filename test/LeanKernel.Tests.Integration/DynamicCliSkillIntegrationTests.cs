using System.Collections.Concurrent;
using System.Text;

using FluentAssertions;

using LeanKernel.Logic.Tools;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LeanKernel.Tests.Integration;

public class DynamicCliSkillIntegrationTests
{
    [Fact]
    public async Task CliSkill_LoadsIntoRegistry_AndExecutesTool()
    {
        var scriptPath = CreateExecutableScript("printf '%s\\n' \"$@\"");

        try
        {
            var skill = BuildCliSkillManifest(
                skillName: "blog",
                command: scriptPath,
                operationId: "create",
                summary: "Create draft");

            using var factory = new DynamicCliSkillTestApplicationFactory(skill);
            var registry = factory.Services.GetRequiredService<IToolRegistry>();

            var tool = registry.Tools.Single(t => t.Name == "blog_create");
            var result = await tool.Handler(
                new Dictionary<string, object?> { ["title"] = "Hello" },
                CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Be("create\n--title\nHello\n");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(scriptPath)!, recursive: true);
        }
    }

    [Fact]
    public void CliSkill_MissingBinary_LogsWarning_AndSkipsRegistration()
    {
        var skill = BuildCliSkillManifest(
            skillName: "missing",
            command: "lk-nonexistent-cli-binary-12345",
            operationId: "run",
            summary: "Run missing tool");

        using var factory = new DynamicCliSkillTestApplicationFactory(skill);
        var registry = factory.Services.GetRequiredService<IToolRegistry>();

        registry.Tools.Select(t => t.Name).Should().NotContain("missing_run");
        factory.LogEntries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("command 'lk-nonexistent-cli-binary-12345' not found on PATH", StringComparison.Ordinal)
            && entry.Message.Contains("CLI tool 'missing'", StringComparison.Ordinal));
    }

    [Fact]
    public void CliSkill_WithAllowHosts_LogsAdvisoryWarning_AndStillRegisters()
    {
        var scriptPath = CreateExecutableScript("printf '%s\\n' \"$@\"");

        try
        {
            var skill = BuildCliSkillManifest(
                skillName: "networked",
                command: scriptPath,
                operationId: "run",
                summary: "Run command",
                allowedHosts: ["api.example.com"]);

            using var factory = new DynamicCliSkillTestApplicationFactory(skill);
            var registry = factory.Services.GetRequiredService<IToolRegistry>();

            registry.Tools.Select(t => t.Name).Should().Contain("networked_run");
            factory.LogEntries.Should().Contain(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("declares egress.allowHosts", StringComparison.Ordinal)
                && entry.Message.Contains("enforcement is advisory", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(scriptPath)!, recursive: true);
        }
    }

    [Fact]
    public async Task CliSkill_MaxOutputChars_TruncatesReturnedOutput()
    {
        var scriptPath = CreateExecutableScript("printf '1234567890abcdefghij'");

        try
        {
            var skill = BuildCliSkillManifest(
                skillName: "truncate",
                command: scriptPath,
                operationId: "run",
                summary: "Emit long output");

            using var factory = new DynamicCliSkillTestApplicationFactory(skill, maxOutputChars: 10);
            var registry = factory.Services.GetRequiredService<IToolRegistry>();

            var tool = registry.Tools.Single(t => t.Name == "truncate_run");
            var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Be("1234567890");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(scriptPath)!, recursive: true);
        }
    }

    private static string CreateExecutableScript(string body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "lk-cli-integration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "test-cli.sh");
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");

        if (!OperatingSystem.IsWindows())
        {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }

        return path;
    }

    private static string BuildCliSkillManifest(
        string skillName,
        string command,
        string operationId,
        string summary,
        IReadOnlyList<string>? allowedHosts = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"name: {skillName}");
        builder.AppendLine($"description: {skillName} integration test skill");
        builder.AppendLine("metadata:");
        builder.AppendLine("  category: ops");
        builder.AppendLine("runtime:");
        builder.AppendLine("  type: cli");
        builder.AppendLine($"  command: \"{command.Replace("\"", "\\\"", StringComparison.Ordinal)}\"");
        builder.AppendLine("  timeoutSeconds: 20");
        builder.AppendLine("  auth:");
        builder.AppendLine("    type: none");

        if (allowedHosts is { Count: > 0 })
        {
            builder.AppendLine("  egress:");
            builder.AppendLine("    allowHosts:");
            foreach (var host in allowedHosts)
            {
                builder.AppendLine($"      - {host}");
            }
        }

        builder.AppendLine("operations:");
        builder.AppendLine($"  - id: {operationId}");
        builder.AppendLine($"    summary: {summary}");
        builder.AppendLine("    invoke:");
        builder.AppendLine($"      argv: [{operationId}]");
        builder.AppendLine("      flags:");
        builder.AppendLine("        title: \"--title\"");
        builder.AppendLine("    parameters:");
        builder.AppendLine("      title:");
        builder.AppendLine("        type: string");
        builder.AppendLine("        description: Draft title");
        builder.AppendLine("        required: false");
        builder.AppendLine("---");

        return builder.ToString();
    }
}

internal sealed class DynamicCliSkillTestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _skillDirectory;
    private readonly ConcurrentQueue<LogEntry> _logEntries = [];
    private readonly int _maxOutputChars;

    public DynamicCliSkillTestApplicationFactory(string skillManifest, int maxOutputChars = 12_000)
    {
        _skillDirectory = Path.Combine(Path.GetTempPath(), "lk-cli-skill-manifests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_skillDirectory);
        File.WriteAllText(Path.Combine(_skillDirectory, "SKILL.md"), skillManifest);
        _maxOutputChars = maxOutputChars;
    }

    public IReadOnlyList<LogEntry> LogEntries => _logEntries.ToList();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddProvider(new ListLoggerProvider(_logEntries));
        });

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.Sources.Clear();
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sqlite"] = "Data Source=:memory:",
                ["OpenAI:ApiKey"] = "test-key",
                ["OpenAI:BaseUrl"] = "http://localhost:1",
                ["OpenAI:DefaultModel"] = "test-model",
                ["Agents:DefaultName"] = "leankernel",
                ["Agents:DefaultDescription"] = "Test agent",
                ["Agents:DefaultInstructions"] = "You are a test assistant.",
                ["Agents:Tools:Enabled"] = "true",
                ["Agents:Tools:BuiltIns:Calculation:Enabled"] = "true",
                ["Agents:Tools:BuiltIns:Calculation:MaxInputItems"] = "100",
                ["Agents:Tools:DynamicCli:MaxOutputChars"] = _maxOutputChars.ToString(),
                ["Agents:Tools:SkillBasePaths:0"] = _skillDirectory,
                ["Files:RootPath"] = Path.GetTempPath(),
                ["GBrain:BaseUrl"] = "http://localhost:1",
                ["GBrain:TimeoutSeconds"] = "1"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            var databaseName = $"DynamicCliSkillTests_{Guid.NewGuid():N}";

            var entityType = typeof(LeanKernel.Data.EntityContext);
            var optionsConfigType = typeof(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<>)
                .MakeGenericType(entityType);

            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<LeanKernel.Data.EntityContext>) ||
                d.ServiceType == entityType ||
                d.ServiceType == typeof(IDbContextFactory<LeanKernel.Data.EntityContext>) ||
                d.ServiceType == optionsConfigType ||
                d.ServiceType == typeof(Microsoft.EntityFrameworkCore.Infrastructure.ServiceProviderAccessor)).ToList();

            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<LeanKernel.Data.EntityContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            services.AddDbContextFactory<LeanKernel.Data.EntityContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            services.Configure<HealthCheckServiceOptions>(opts =>
            {
                var external = opts.Registrations
                    .Where(r => r.Name is "litellm" or "gbrain")
                    .ToList();

                foreach (var registration in external)
                {
                    opts.Registrations.Remove(registration);
                }
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        if (Directory.Exists(_skillDirectory))
        {
            Directory.Delete(_skillDirectory, recursive: true);
        }
    }
}

internal sealed record LogEntry(LogLevel Level, string Category, string Message);

internal sealed class ListLoggerProvider(ConcurrentQueue<LogEntry> entries) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ListLogger(entries, categoryName);

    public void Dispose()
    {
    }
}

internal sealed class ListLogger(ConcurrentQueue<LogEntry> entries, string categoryName) : ILogger
{
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        entries.Enqueue(new LogEntry(logLevel, categoryName, formatter(state, exception)));
    }
}

internal sealed class NullScope : IDisposable
{
    public static readonly NullScope Instance = new();

    public void Dispose()
    {
    }
}
