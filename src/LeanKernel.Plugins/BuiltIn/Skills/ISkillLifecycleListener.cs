namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Interface for components that want to react to skill availability changes.
/// Implemented by consumers to receive lifecycle events when skills are loaded, updated, or become unavailable.
/// </summary>
public interface ISkillLifecycleListener
{
    /// <summary>
    /// Called when a skill becomes available (loaded successfully or recovered).
    /// </summary>
    /// <param name="skillName">Name of the skill</param>
    /// <param name="skill">The skill definition</param>
    /// <param name="ct">Cancellation token</param>
    Task OnSkillAvailableAsync(string skillName, SkillDefinition skill, CancellationToken ct);

    /// <summary>
    /// Called when a skill becomes unavailable (validation errors, missing binaries, etc).
    /// </summary>
    /// <param name="skillName">Name of the skill</param>
    /// <param name="reason">Reason why the skill is unavailable</param>
    /// <param name="ct">Cancellation token</param>
    Task OnSkillUnavailableAsync(string skillName, string reason, CancellationToken ct);

    /// <summary>
    /// Called when skills are reloaded from disk (e.g., file watcher detected changes).
    /// </summary>
    /// <param name="skillNames">Names of skills that were reloaded</param>
    /// <param name="ct">Cancellation token</param>
    Task OnSkillsReloadedAsync(IReadOnlyList<string> skillNames, CancellationToken ct);
}
