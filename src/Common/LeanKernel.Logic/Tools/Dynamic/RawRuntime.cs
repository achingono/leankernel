using System.Text.RegularExpressions;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LeanKernel.Logic.Tools.Dynamic;

internal sealed class RawRuntime
{
    public string? Type { get; set; }
    public string? BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; }
    public RawAuth? Auth { get; set; }
    public RawEgress? Egress { get; set; }
}
#pragma warning restore CS8618