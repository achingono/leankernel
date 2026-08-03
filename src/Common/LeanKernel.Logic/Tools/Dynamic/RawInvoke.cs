namespace LeanKernel.Logic.Tools.Dynamic;

internal sealed class RawInvoke
{
    public string? HttpMethod { get; set; }

    public string? HttpPath { get; set; }

    public List<string>? Argv { get; set; }

    public Dictionary<string, string?>? Flags { get; set; }
}
#pragma warning restore CS8618