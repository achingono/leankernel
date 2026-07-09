using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Parses and resolves Signal-specific attachment directives from model responses.
/// </summary>
public interface ISignalAttachmentParser
{
    /// <summary>
    /// Attempts to find and remove a signal-attachments directive from the response content,
    /// resolving the referenced attachments against the set of incoming attachments.
    /// </summary>
    /// <param name="content">The raw response content. The directive is removed if found.</param>
    /// <param name="attachments">The resolved attachments, if a valid directive was found.</param>
    /// <param name="incomingAttachments">The incoming attachments to resolve against.</param>
    /// <returns><c>true</c> if a directive was found and resolved; otherwise <c>false</c>.</returns>
    bool TryParseAndRemoveDirective(
        ref string content,
        out IReadOnlyList<Attachment>? attachments,
        IReadOnlyList<Attachment>? incomingAttachments);
}
