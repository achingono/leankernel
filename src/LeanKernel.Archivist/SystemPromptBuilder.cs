using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Archivist;

/// <summary>
/// Builds the system prompt from persisted agent identity, user identity, and known capability gaps.
/// </summary>
public sealed class SystemPromptBuilder
{
    private const string DefaultSystemPrompt = """
        You are an AI assistant and a user's personal agent.

                Operating principle: be useful by default through verified execution, not promises.
                - Never claim a file was created, updated, or deleted unless you have actually completed that action.
                - If the user points out a missed action, verify current state and self-correct immediately.
                - If local file tools are available, use them directly for requested file operations.
                - Do not tell the user to manually create files unless file operations are unavailable or explicitly denied by policy.
                - Update engagement identity files (AGENTS.md, SELF.md, USER.md) when corrective patterns are learned,
                    unless engagement policy explicitly requires permission first.
                - If USER.md or SELF.md are missing, initialize them before continuing normal work.
        
        Your goal is to understand their needs and preferences by asking clarifying questions:
        - What would you like my name to be?
        - What is your preferred engagement model? (e.g., proactive, reactive, advisory)
        - What are your communication preferences? (tone, formality, detail level)
        - What actions should I handle autonomously vs. asking for permission?
        - What are your availability and timezone preferences?
        
        Once you understand their preferences, help them configure SELF.md and USER.md.
        Answer concisely and accurately using only the context provided.
        If you don't have enough context, ask clarifying questions rather than guessing.
        Structure important facts as Who/What/Where/When/Why/How.
        """;

    private readonly LeanKernelConfig _config;
    private readonly ICapabilityGapStore? _capabilityGapStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPromptBuilder" /> class.
    /// </summary>
    /// <param name="config">The LeanKernel configuration that identifies the active agent directory.</param>
    /// <param name="capabilityGapStore">The optional capability-gap store used to enrich prompts.</param>
    public SystemPromptBuilder(
        IOptions<LeanKernelConfig> config,
        ICapabilityGapStore? capabilityGapStore = null)
    {
        _config = config.Value;
        _capabilityGapStore = capabilityGapStore;
    }

    /// <summary>
    /// Builds the system prompt for the active agent.
    /// </summary>
    /// <param name="ct">A token used to cancel prompt file reads.</param>
    /// <returns>The trimmed system prompt text.</returns>
    public async Task<string> BuildAsync(CancellationToken ct)
    {
        var agentDir = Path.Combine(_config.Agents.BasePath, "main");
        var soulPath = Path.Combine(agentDir, "SELF.md");
        var userPath = Path.Combine(agentDir, "USER.md");

        var soulContent = File.Exists(soulPath)
            ? await File.ReadAllTextAsync(soulPath, ct)
            : null;

        var userContent = File.Exists(userPath)
            ? await File.ReadAllTextAsync(userPath, ct)
            : null;

        if (soulContent is null && userContent is null)
        {
            return AppendFilesystemLocations(DefaultSystemPrompt);
        }

        var sb = new System.Text.StringBuilder();

        if (soulContent is not null)
        {
            sb.AppendLine(soulContent);
        }
        else
        {
            sb.AppendLine(DefaultSystemPrompt);
        }

        if (userContent is not null)
        {
            sb.AppendLine();
            sb.AppendLine(userContent);
        }

        if (_capabilityGapStore is not null)
        {
            var capabilityGaps = await _capabilityGapStore.ReadPromptSectionAsync(ct);
            if (!string.IsNullOrWhiteSpace(capabilityGaps))
            {
                sb.AppendLine();
                sb.AppendLine(capabilityGaps);
            }
        }

        return AppendFilesystemLocations(sb.ToString().Trim());
    }

    private string AppendFilesystemLocations(string prompt)
    {
        var agentMainPath = Path.Combine(_config.Agents.BasePath, "main");
        var dataRoot = Path.GetDirectoryName(_config.Wiki.BasePath) ?? "/app/data";
        var locations = new (string Label, string Path)[]
        {
            ("Data root", dataRoot),
            ("Agent identity folder", agentMainPath),
            ("Agents base folder", _config.Agents.BasePath),
            ("Knowledge documents folder", _config.Knowledge.DocumentsPath),
            ("Wiki folder", _config.Wiki.BasePath)
        };

        var sb = new System.Text.StringBuilder(prompt.Trim());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Configured filesystem locations");
        sb.AppendLine("Use these configured data folders when searching for or updating local files. If the exact path is unknown, use file_search first, then use file_read, file_write, or file_edit with the discovered path.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (label, path) in locations)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!seen.Add($"{label}:{normalized}"))
            {
                continue;
            }

            sb.AppendLine($"- {label}: {normalized}");
        }

        sb.AppendLine("- Important identity files: AGENTS.md, SELF.md, and USER.md are under the agent identity folder.");
        return sb.ToString().Trim();
    }
}
