using System.Text.RegularExpressions;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LeanKernel.Logic.Tools.Dynamic;

internal sealed class RawInvoke
{
    public string? HttpMethod { get; set; }
    public string? HttpPath { get; set; }
}
#pragma warning restore CS8618