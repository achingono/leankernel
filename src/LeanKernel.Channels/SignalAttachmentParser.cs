using System.Text.Json;
using System.Text.RegularExpressions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Channels;

/// <summary>
/// Parses Signal-specific attachment directives embedded in model responses
/// and resolves them against the turn's incoming attachments.
/// </summary>
internal sealed class SignalAttachmentParser(ILogger<SignalAttachmentParser> logger) : ISignalAttachmentParser
{
    private static readonly Regex DirectiveRegex = new(
        "```signal-attachments\\s*(?<json>\\{[\\s\\S]*?\\})\\s*```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(200));

    private readonly ILogger<SignalAttachmentParser> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool TryParseAndRemoveDirective(
        ref string content,
        out IReadOnlyList<Attachment>? attachments,
        IReadOnlyList<Attachment>? incomingAttachments)
    {
        ArgumentNullException.ThrowIfNull(content);

        var match = DirectiveRegex.Match(content);
        if (!match.Success)
        {
            attachments = null;
            return false;
        }

        content = DirectiveRegex.Replace(content, string.Empty, 1).TrimEnd();

        if (incomingAttachments is not { Count: > 0 })
        {
            _logger.LogWarning("Signal attachment directive was ignored because no incoming attachments were present");
            attachments = null;
            return true;
        }

        try
        {
            var directive = JsonSerializer.Deserialize<SignalDirective>(
                match.Groups["json"].Value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            attachments = ResolveAttachments(directive, incomingAttachments);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse signal-attachments directive; sending text-only response");
            attachments = null;
            return true;
        }
    }

    private static List<Attachment> ResolveAttachments(
        SignalDirective? directive,
        IReadOnlyList<Attachment> incomingAttachments)
    {
        if (directive?.Attachments is not { Count: > 0 })
        {
            return [];
        }

        var resolved = new List<Attachment>(directive.Attachments.Count);
        foreach (var entry in directive.Attachments)
        {
            if (!string.Equals(entry.Source, "incoming", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var zeroBasedIndex = entry.Index - 1;
            if (zeroBasedIndex < 0 || zeroBasedIndex >= incomingAttachments.Count)
            {
                continue;
            }

            resolved.Add(incomingAttachments[zeroBasedIndex]);
        }

        return resolved;
    }

    private sealed class SignalDirective
    {
        public List<SignalDirectiveEntry> Attachments { get; set; } = [];
    }

    private sealed class SignalDirectiveEntry
    {
        public string Source { get; set; } = string.Empty;

        public int Index { get; set; }
    }
}
