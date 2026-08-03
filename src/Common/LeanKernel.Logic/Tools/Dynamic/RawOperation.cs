namespace LeanKernel.Logic.Tools.Dynamic;

internal sealed class RawOperation
{
    public string? Id { get; set; }

    public string? Summary { get; set; }

    public RawInvoke? Invoke { get; set; }

    /// <summary>
    /// The raw parameters mapping. Supports both the flat Phase 01 format
    /// (<c>name: {type, description, required}</c>) and the JSON-schema style
    /// (<c>{type: object, properties: {...}, required: [...]}</c>).
    /// </summary>
    public object? Parameters { get; set; }
}
#pragma warning restore CS8618