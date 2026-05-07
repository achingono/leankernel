using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using LeanKernel.Host.Models.Routing;

namespace LeanKernel.Host.Services;

public interface ILiteLlmRoutingConfigService
{
    LiteLlmRoutingConfig Load();
    List<RoutingValidationError> Validate(LiteLlmRoutingConfig config);
    List<ProviderKeyStatus> GetKeyStatuses(LiteLlmRoutingConfig config);
    string GenerateYaml(LiteLlmRoutingConfig config);
    string ComputeDiff(string oldYaml, string newYaml);
    Task SaveAsync(LiteLlmRoutingConfig config, CancellationToken ct);
}

public sealed class LiteLlmRoutingConfigService : ILiteLlmRoutingConfigService
{
    private readonly string _configPath;

    public LiteLlmRoutingConfigService(string configPath)
    {
        _configPath = configPath;
    }

    public LiteLlmRoutingConfig Load()
    {
        if (!File.Exists(_configPath))
            return new LiteLlmRoutingConfig();

        var yaml = File.ReadAllText(_configPath);
        return ParseYaml(yaml);
    }

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

    public string GenerateYaml(LiteLlmRoutingConfig config)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)
            .Build();

        return serializer.Serialize(config);
    }

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

    public async Task SaveAsync(LiteLlmRoutingConfig config, CancellationToken ct)
    {
        var yaml = GenerateYaml(config);
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(_configPath, yaml, ct);
    }

    public static LiteLlmRoutingConfig ParseYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<LiteLlmRoutingConfig>(yaml) ?? new LiteLlmRoutingConfig();
    }
}
