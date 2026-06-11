using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace LeanKernel.Plugins.BuiltIn.Skills;

public sealed partial class SkillParser
{
    private static readonly Regex FrontmatterRegex = FrontmatterPattern();

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
        .Build();

    public SkillDefinition? Parse(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            return null;

        var content = File.ReadAllText(filePath);
        return ParseContent(content, filePath);
    }

    public SkillDefinition? ParseContent(string content, string? sourcePath = null)
    {
        var match = FrontmatterRegex.Match(content);
        if (!match.Success)
            return null;

        var yaml = match.Groups[1].Value;

        try
        {
            var raw = _deserializer.Deserialize<RawSkill>(yaml);
            if (raw is null || string.IsNullOrWhiteSpace(raw.Name))
                return null;

            var operations = raw.Operations?.Select(o =>
            {
                var invoke = o.Invoke ?? new RawInvoke();
                return new SkillOperation
                {
                    Id = o.Id ?? string.Empty,
                    Summary = o.Summary ?? string.Empty,
                    Invoke = new SkillInvokeConfig
                    {
                        Argv = invoke.Argv ?? [],
                        Flags = invoke.Flags ?? new Dictionary<string, string>(),
                        HttpMethod = invoke.HttpMethod,
                        HttpPath = invoke.HttpPath
                    },
                    ParametersRaw = ConvertParameters(o.Parameters)
                };
            }).ToList();

            if (operations is null || operations.Count == 0)
                return null;

            var runtime = raw.Runtime ?? new RawRuntime();

            return new SkillDefinition
            {
                Name = raw.Name,
                Description = raw.Description ?? string.Empty,
                Metadata = raw.Metadata ?? new Dictionary<string, object?>(),
                Runtime = new SkillRuntimeConfig
                {
                    Type = runtime.Type ?? "cli",
                    Command = runtime.Command,
                    BaseUrl = runtime.BaseUrl,
                    TimeoutSeconds = runtime.TimeoutSeconds > 0 ? runtime.TimeoutSeconds : 30,
                    Auth = new SkillAuthConfig
                    {
                        Type = runtime.Auth?.Type ?? "none",
                        SecretRef = runtime.Auth?.SecretRef
                    },
                    Requires = new SkillRequiresConfig
                    {
                        Bins = runtime.Requires?.Bins?.Select(b => new SkillBinConfig
                        {
                            Name = b.Name ?? string.Empty,
                            MinVersion = b.MinVersion,
                            ChecksumSha256 = b.ChecksumSha256
                        }).ToList() ?? new List<SkillBinConfig>()
                    },
                    Egress = new SkillEgressConfig
                    {
                        AllowHosts = runtime.Egress?.AllowHosts ?? new List<string>()
                    }
                },
                Operations = operations,
                SourcePath = sourcePath
            };
        }
        catch (YamlException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?>? ConvertParameters(object? raw)
    {
        if (raw is null)
            return null;

        if (raw is Dictionary<object, object?> dict)
        {
            return dict.ToDictionary(kvp => kvp.Key?.ToString() ?? string.Empty, kvp => kvp.Value);
        }

        return null;
    }

    [GeneratedRegex(@"^---\s*\n(.*?)\n---", RegexOptions.Singleline)]
    private static partial Regex FrontmatterPattern();

    private sealed record RawSkill
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public Dictionary<string, object?>? Metadata { get; init; }
        public RawRuntime? Runtime { get; init; }
        public List<RawOperation>? Operations { get; init; }
    }

    private sealed record RawRuntime
    {
        public string? Type { get; init; }
        public string? Command { get; init; }
        public string? BaseUrl { get; init; }
        public int TimeoutSeconds { get; init; }
        public RawAuth? Auth { get; init; }
        public RawRequires? Requires { get; init; }
        public RawEgress? Egress { get; init; }
    }

    private sealed record RawAuth
    {
        public string? Type { get; init; }
        public string? SecretRef { get; init; }
    }

    private sealed record RawRequires
    {
        public List<RawBin>? Bins { get; init; }
    }

    private sealed record RawBin
    {
        public string? Name { get; init; }
        public string? MinVersion { get; init; }
        public string? ChecksumSha256 { get; init; }
    }

    private sealed record RawEgress
    {
        public List<string>? AllowHosts { get; init; }
    }

    private sealed record RawOperation
    {
        public string? Id { get; init; }
        public string? Summary { get; init; }
        public RawInvoke? Invoke { get; init; }
        public object? Parameters { get; init; }
    }

    private sealed record RawInvoke
    {
        public List<string>? Argv { get; init; }
        public Dictionary<string, string>? Flags { get; init; }
        public string? HttpMethod { get; init; }
        public string? HttpPath { get; init; }
    }
}
