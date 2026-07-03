namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a model-emitted task status directive.
/// </summary>
public sealed record TaskStatusDirective(string Status, string? Note);
