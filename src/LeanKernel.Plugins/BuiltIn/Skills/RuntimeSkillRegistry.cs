using LeanKernel.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Interface for loading and discovering skills from the filesystem.
/// </summary>
public interface ISkillRegistry
{
    Task<SkillDefinition?> GetSkillAsync(string skillName);
    Task<IReadOnlyDictionary<string, SkillDefinition>> GetAllSkillsAsync();
    Task RefreshSkillsAsync();
    Task InitializeAsync(IEnumerable<string> skillDirectories);
    List<string> GetQuarantinedSkills();
}

/// <summary>
/// Runtime skill registry that discovers SKILL.md files and caches definitions.
/// Allows skills to be added/updated without recompiling.
/// </summary>
public sealed class RuntimeSkillRegistry : ISkillRegistry
{
    private readonly SkillParser _parser;
    private readonly IBinaryResolver _binaryResolver;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RuntimeSkillRegistry> _logger;
    private readonly HashSet<string> _skillDirectories;
    private const string CACHE_KEY = "skills:all";
    private const string QUARANTINED_KEY = "skills:quarantined";
    private const int CACHE_DURATION_MINUTES = 60;

    public RuntimeSkillRegistry(
        SkillParser parser,
        IBinaryResolver binaryResolver,
        IMemoryCache cache,
        ILogger<RuntimeSkillRegistry> logger)
    {
        _parser = parser;
        _binaryResolver = binaryResolver;
        _cache = cache;
        _logger = logger;
        _skillDirectories = [];
    }

    /// <summary>
    /// Initialize the registry with skill directories.
    /// Should be called once at startup.
    /// </summary>
    public async Task InitializeAsync(IEnumerable<string> skillDirectories)
    {
        foreach (var dir in skillDirectories)
        {
            if (Directory.Exists(dir))
            {
                _skillDirectories.Add(dir);
            }
        }

        // Trigger discovery
        await GetAllSkillsAsync();
        _logger.LogInformation("RuntimeSkillRegistry initialized with {Count} directories", _skillDirectories.Count);
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

    public List<string> GetQuarantinedSkills()
    {
        if (_cache.TryGetValue(QUARANTINED_KEY, out List<string>? quarantined))
            return quarantined ?? [];
        return [];
    }

    public Task RefreshSkillsAsync()
    {
        _cache.Remove(CACHE_KEY);
        _cache.Remove(QUARANTINED_KEY);
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
        _logger.LogDebug("Skill file changed, refreshing cache");
        _cache.Remove(CACHE_KEY);
    }

    private async Task<Dictionary<string, SkillDefinition>> DiscoverSkillsAsync()
    {
        var skills = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);
        var quarantined = new List<string>();

        foreach (var directory in _skillDirectories)
        {
            var skillFiles = Directory.GetFiles(directory, "SKILL.md", SearchOption.AllDirectories);

            foreach (var filePath in skillFiles)
            {
                await DiscoverSkillFileAsync(filePath, skills, quarantined);
            }
        }

        _logger.LogInformation("Discovered {Count} skills ({Available} available)", skills.Count, skills.Values.Count(s => s.IsAvailable));
        if (quarantined.Count > 0)
        {
            _logger.LogWarning("Quarantined {Count} invalid skills", quarantined.Count);
            foreach (var item in quarantined)
                _logger.LogWarning("  - {QuarantinedSkill}", item);

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CACHE_DURATION_MINUTES)
            };
            _cache.Set(QUARANTINED_KEY, quarantined, cacheOptions);
        }

        return skills;
    }

    private async Task DiscoverSkillFileAsync(
        string filePath,
        Dictionary<string, SkillDefinition> skills,
        List<string> quarantined)
    {
        try
        {
            var definition = await _parser.ParseSkillFileAsync(filePath);
            if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
                return;

            if (QuarantineIfInvalid(definition, quarantined))
                return;

            definition = ApplyAvailability(definition);
            skills[definition.Name] = definition;
            _logger.LogInformation(
                "Loaded skill: {SkillName} from {FilePath} (available: {Available})",
                definition.Name,
                filePath,
                definition.IsAvailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse skill from {FilePath}", filePath);
            var skillName = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "unknown";
            quarantined.Add($"{skillName}: parse error - {ex.Message}");
        }
    }

    private bool QuarantineIfInvalid(SkillDefinition definition, List<string> quarantined)
    {
        if (definition.ValidationErrors.Count == 0)
            return false;

        var errors = string.Join("; ", definition.ValidationErrors);
        _logger.LogWarning(
            "Skill {SkillName} has validation errors and is quarantined: {Errors}",
            definition.Name,
            errors);
        quarantined.Add($"{definition.Name}: {errors}");
        return true;
    }

    private SkillDefinition ApplyAvailability(SkillDefinition definition)
    {
        var unavailableReason = CheckBinaryAvailability(definition);
        if (unavailableReason == null)
            return definition;

        _logger.LogWarning("Skill {SkillName} is unavailable: {Reason}", definition.Name, unavailableReason);
        return definition with
        {
            IsAvailable = false,
            UnavailableReason = unavailableReason
        };
    }

    /// <summary>
    /// Check if all required binaries for a skill are available.
    /// Returns null if all are available, or an error message if not.
    /// </summary>
    private string? CheckBinaryAvailability(SkillDefinition definition)
    {
        if (definition.Runtime?.Requires.Bins.Count == 0)
            return null;

        var missingBins = new List<string>();
        foreach (var bin in definition.Runtime?.Requires.Bins ?? [])
        {
            if (!_binaryResolver.IsBinaryAvailable(bin.Name, bin.MinVersion))
                missingBins.Add($"{bin.Name} (required: {bin.MinVersion ?? "any"})");
        }

        return missingBins.Count > 0 ? $"Missing binaries: {string.Join(", ", missingBins)}" : null;
    }
}
