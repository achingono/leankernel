using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using LeanKernel.Host.Models.Routing;

namespace LeanKernel.Host.Services;

/// <summary>
/// Defines the contract for ilite llm routing config service.
/// </summary>
public interface ILiteLlmRoutingConfigService
{
    /// <summary>
    /// Loads  information.
    /// </summary>
    LiteLlmRoutingConfig Load();
    /// <summary>
    /// Validates  information.
    /// </summary>
    List<RoutingValidationError> Validate(LiteLlmRoutingConfig config);
    /// <summary>
    /// Gets key statuses information.
    /// </summary>
    List<ProviderKeyStatus> GetKeyStatuses(LiteLlmRoutingConfig config);
    /// <summary>
    /// Gets or performs the generate yaml operation.
    /// </summary>
    string GenerateYaml(LiteLlmRoutingConfig config);
    /// <summary>
    /// Gets or performs the compute diff operation.
    /// </summary>
    string ComputeDiff(string oldYaml, string newYaml);
    /// <summary>
    /// Saves async information.
    /// </summary>
    Task SaveAsync(LiteLlmRoutingConfig config, CancellationToken ct);
    /// <summary>
    /// Gets raw yaml information.
    /// </summary>
    string GetRawYaml();
    /// <summary>
    /// Saves raw yaml async information.
    /// </summary>
    Task SaveRawYamlAsync(string yaml, CancellationToken ct);
}

/// <summary>
/// Represents the lite llm routing config service.
/// </summary>
public sealed class LiteLlmRoutingConfigService : ILiteLlmRoutingConfigService
{
    private readonly string _configPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteLlmRoutingConfigService" /> class.
    /// </summary>
    /// <param name="configPath">The config path.</param>
    public LiteLlmRoutingConfigService(string configPath)
    {
        _configPath = configPath;
    }

    /// <summary>
    /// Executes the load operation.
    /// </summary>
    /// <returns>The operation result.</returns>
    public LiteLlmRoutingConfig Load()
    {
        if (!File.Exists(_configPath))
            return new LiteLlmRoutingConfig();

        var yaml = File.ReadAllText(_configPath);
        return ParseYaml(yaml);
    }

    /// <summary>
    /// Executes the validate operation.
    /// </summary>
    /// <param name="config">The config.</param>
    /// <returns>The operation result.</returns>
    public List<RoutingValidationError> Validate(LiteLlmRoutingConfig config)
    {
        var errors = new List<RoutingValidationError>();

        // Validate route references
        foreach (var (laneName, entries) in config.Routes)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                if (!config.Providers.ContainsKey(entry.Provider))
                {
                    errors.Add(new RoutingValidationError
                    {
                        Code = "UNKNOWN_PROVIDER",
                        Message = $"Route {laneName}[{i}] references unknown provider '{entry.Provider}'.",
                        Severity = "error"
                    });
                    continue;
                }

                var provider = config.Providers[entry.Provider];
                if (!provider.Models.Any(m => m.Id == entry.Model))
                {
                    errors.Add(new RoutingValidationError
                    {
                        Code = "UNKNOWN_MODEL",
                        Message = $"Route {laneName}[{i}] references unknown model '{entry.Model}' for provider '{entry.Provider}'.",
                        Severity = "error"
                    });
                }
            }

            // Detect duplicate order values
            var duplicateOrders = entries
                .GroupBy(e => e.Order)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var order in duplicateOrders)
            {
                errors.Add(new RoutingValidationError
                {
                    Code = "DUPLICATE_ORDER",
                    Message = $"Route lane '{laneName}' has multiple entries with order={order}.",
                    Severity = "warning"
                });
            }
        }

        // Validate alias targets
        foreach (var (alias, target) in config.Aliases)
        {
            if (!config.Routes.ContainsKey(target))
            {
                errors.Add(new RoutingValidationError
                {
                    Code = "INVALID_ALIAS_TARGET",
                    Message = $"Alias '{alias}' points to unknown route '{target}'.",
                    Severity = "error"
                });
            }
        }

        return errors;
    }

    /// <summary>
    /// Executes the get key statuses operation.
    /// </summary>
    /// <param name="config">The config.</param>
    /// <returns>The operation result.</returns>
    public List<ProviderKeyStatus> GetKeyStatuses(LiteLlmRoutingConfig config)
    {
        var statuses = new List<ProviderKeyStatus>();

        foreach (var (providerName, provider) in config.Providers)
        {
            var slots = provider.Keys ?? [];
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var envVar = slot.Name ?? "";
                var configured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVar));
                statuses.Add(new ProviderKeyStatus
                {
                    Provider = providerName,
                    SlotName = $"{providerName}{i + 1}",
                    EnvVar = envVar,
                    Configured = configured,
                    Enabled = slot.Enabled
                });
            }
        }

        return statuses;
    }

    /// <summary>
    /// Executes the generate yaml operation.
    /// </summary>
    /// <param name="config">The config.</param>
    /// <returns>The operation result.</returns>
    public string GenerateYaml(LiteLlmRoutingConfig config)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)
            .Build();

        return serializer.Serialize(config);
    }

    /// <summary>
    /// Executes the compute diff operation.
    /// </summary>
    /// <param name="oldYaml">The old yaml.</param>
    /// <param name="newYaml">The new yaml.</param>
    /// <returns>The operation result.</returns>
    public string ComputeDiff(string oldYaml, string newYaml)
    {
        var oldLines = oldYaml.Split('\n');
        var newLines = newYaml.Split('\n');

        var sb = new StringBuilder();
        var maxLen = Math.Max(oldLines.Length, newLines.Length);

        for (int i = 0; i < maxLen; i++)
        {
            var oldLine = i < oldLines.Length ? oldLines[i] : null;
            var newLine = i < newLines.Length ? newLines[i] : null;

            if (oldLine == newLine) continue;

            if (oldLine != null && newLine == null)
                sb.AppendLine($"- {oldLine}");
            else if (oldLine == null && newLine != null)
                sb.AppendLine($"+ {newLine}");
            else
            {
                sb.AppendLine($"- {oldLine}");
                sb.AppendLine($"+ {newLine}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Executes the save async operation.
    /// </summary>
    /// <param name="config">The config.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SaveAsync(LiteLlmRoutingConfig config, CancellationToken ct)
    {
        var yaml = GenerateYaml(config);
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(_configPath, yaml, ct);
    }

    /// <summary>
    /// Executes the get raw yaml operation.
    /// </summary>
    /// <returns>The operation result.</returns>
    public string GetRawYaml()
    {
        if (File.Exists(_configPath))
            return File.ReadAllText(_configPath);

        // Fall back to the example config as a starter template
        var examplePath = Path.Combine(
            Path.GetDirectoryName(_configPath) ?? "",
            "config.example.yaml");

        if (File.Exists(examplePath))
            return "# Starter template — edit and click Save YAML to apply.\n"
                   + "# This will be written to: " + _configPath + "\n\n"
                   + File.ReadAllText(examplePath);

        return "# LiteLLM config.yaml\n"
               + "# File not found: " + _configPath + "\n"
               + "# Add a 'providers' block to get started.\n";
    }

    /// <summary>
    /// Executes the save raw yaml async operation.
    /// </summary>
    /// <param name="yaml">The yaml.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SaveRawYamlAsync(string yaml, CancellationToken ct)
    {
        // Validate by parsing first
        ParseYaml(yaml); // throws if invalid
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(_configPath, yaml, ct);
    }

    /// <summary>
    /// Executes the parse yaml operation.
    /// </summary>
    /// <param name="yaml">The yaml.</param>
    /// <returns>The operation result.</returns>
    public static LiteLlmRoutingConfig ParseYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<LiteLlmRoutingConfig>(yaml) ?? new LiteLlmRoutingConfig();
    }
}
