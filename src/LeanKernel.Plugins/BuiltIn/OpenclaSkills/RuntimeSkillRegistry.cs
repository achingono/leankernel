using LeanKernel.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Plugins.BuiltIn.OpenclaSkills;

/// <summary>
/// Interface for loading and discovering skills from the filesystem.
/// </summary>
public interface ISkillRegistry
{
    Task<SkillDefinition?> GetSkillAsync(string skillName);
    Task<IReadOnlyDictionary<string, SkillDefinition>> GetAllSkillsAsync();
    Task RefreshSkillsAsync();
    Task WatchSkillDirectoryAsync(string directory, CancellationToken ct);
}

/// <summary>
/// Runtime skill registry that discovers SKILL.md files and caches definitions.
/// Allows skills to be added/updated without recompiling.
/// </summary>
public sealed class RuntimeSkillRegistry : ISkillRegistry
{
    private readonly SkillParser _parser;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RuntimeSkillRegistry> _logger;
    private readonly HashSet<string> _skillDirectories;
    private const string CACHE_KEY = "skills:all";
    private const int CACHE_DURATION_MINUTES = 60;

    public RuntimeSkillRegistry(
        SkillParser parser,
        IMemoryCache cache,
        ILogger<RuntimeSkillRegistry> logger)
    {
        _parser = parser;
        _cache = cache;
        _logger = logger;
        _skillDirectories = [];
    }

    public async Task<SkillDefinition?> GetSkillAsync(string skillName)
    {
        var allSkills = await GetAllSkillsAsync();
        return allSkills.TryGetValue(skillName, out var skill) ? skill : null;
    }

    public async Task<IReadOnlyDictionary<string, SkillDefinition>> GetAllSkillsAsync()
    {
        if (_cache.TryGetValue(CACHE_KEY, out Dictionary<string, SkillDefinition>? cached))
            return cached ?? [];

        var skills = await DiscoverSkillsAsync();
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CACHE_DURATION_MINUTES)
        };

        _cache.Set(CACHE_KEY, skills, cacheOptions);
        return skills;
    }

    public Task RefreshSkillsAsync()
    {
        _cache.Remove(CACHE_KEY);
        return Task.CompletedTask;
    }

    public async Task WatchSkillDirectoryAsync(string directory, CancellationToken ct)
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Skill directory does not exist: {Directory}", directory);
            return;
        }

        _skillDirectories.Add(directory);

        var watcher = new FileSystemWatcher(directory)
        {
            Filter = "SKILL.md",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };

        watcher.Changed += (_, _) => OnSkillFileChanged();
        watcher.Created += (_, _) => OnSkillFileChanged();
        watcher.Deleted += (_, _) => OnSkillFileChanged();
        watcher.EnableRaisingEvents = true;

        _logger.LogInformation("Watching skill directory: {Directory}", directory);

        // Keep watcher alive until cancellation
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
    }

    private void OnSkillFileChanged()
    {
        _logger.LogInformation("Skill file changed, refreshing cache");
        _cache.Remove(CACHE_KEY);
    }

    private async Task<Dictionary<string, SkillDefinition>> DiscoverSkillsAsync()
    {
        var skills = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in _skillDirectories)
        {
            var skillFiles = Directory.GetFiles(directory, "SKILL.md", SearchOption.AllDirectories);

            foreach (var filePath in skillFiles)
            {
                try
                {
                    var definition = await _parser.ParseSkillFileAsync(filePath);
                    if (definition != null && !string.IsNullOrWhiteSpace(definition.Name))
                    {
                        skills[definition.Name] = definition;
                        _logger.LogInformation("Loaded skill: {SkillName} from {FilePath}", definition.Name, filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load skill from {FilePath}", filePath);
                }
            }
        }

        _logger.LogInformation("Discovered {Count} skills", skills.Count);
        return skills;
    }
}
