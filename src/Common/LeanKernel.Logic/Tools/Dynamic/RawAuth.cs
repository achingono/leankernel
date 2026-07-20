using System.Text.RegularExpressions;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LeanKernel.Logic.Tools.Dynamic;

internal sealed class RawAuth
{
    public string? Type { get; set; }
    public string? SecretRef { get; set; }
}
#pragma warning restore CS8618