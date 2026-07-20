using System.Text.RegularExpressions;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LeanKernel.Logic.Tools.Dynamic;

internal sealed class RawEgress
{
    public List<string>? AllowHosts { get; set; }
}
#pragma warning restore CS8618