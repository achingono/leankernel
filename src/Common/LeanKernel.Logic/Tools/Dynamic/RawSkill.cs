namespace LeanKernel.Logic.Tools.Dynamic;

// Raw YAML DTOs (for deserialization only)
#pragma warning disable CS8618
internal sealed class RawSkill
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    public Dictionary<string, object?>? Metadata { get; set; }

    public RawRuntime? Runtime { get; set; }

    public List<RawOperation>? Operations { get; set; }
}
#pragma warning restore CS8618