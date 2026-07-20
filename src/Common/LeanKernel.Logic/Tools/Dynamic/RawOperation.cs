namespace LeanKernel.Logic.Tools.Dynamic;

internal sealed class RawOperation
{
    public string? Id { get; set; }
    public string? Summary { get; set; }
    public RawInvoke? Invoke { get; set; }
    public Dictionary<string, RawParameter?>? Parameters { get; set; }
}
#pragma warning restore CS8618