using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LeanKernel.Plugins.BuiltIn.Skills;

namespace LeanKernel.Host.Services.Skills;

/// <summary>
/// Hosted service for managing the skill system lifecycle.
/// - Synchronously loads skills during startup (no background async initialization)
/// - Sets up file watchers with debouncing for SKILL.md changes
/// - Notifies lifecycle listeners when skills change
/// </summary>
public sealed class SkillHostedService : IHostedService
{
    private readonly ISkillRegistry _skillRegistry;
    private readonly DynamicPluginHost _pluginHost;
    private readonly IEnumerable<ISkillLifecycleListener> _lifecycleListeners;
    private readonly ILogger<SkillHostedService> _logger;
    private readonly string[] _skillDirectories;
    private CancellationTokenSource? _watcherCts;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, System.Timers.Timer> _debounceTimers = [];
    private const int DEBOUNCE_DELAY_MS = 250;

    public SkillHostedService(
        ISkillRegistry skillRegistry,
        DynamicPluginHost pluginHost,
        IEnumerable<ISkillLifecycleListener> lifecycleListeners,
        ILogger<SkillHostedService> logger,
        string[] skillDirectories)
    {
        _skillRegistry = skillRegistry;
        _pluginHost = pluginHost;
        _lifecycleListeners = lifecycleListeners;
        _logger = logger;
        _skillDirectories = skillDirectories;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Skill Hosted Service");

        // Synchronously initialize skills
        await InitializeSkillsAsync(cancellationToken);

        // Set up watchers for each skill directory
        _watcherCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var directory in _skillDirectories.Where(Directory.Exists))
        {
            SetupFileWatcher(directory, _watcherCts.Token);
        }

        _logger.LogInformation("Skill Hosted Service started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Skill Hosted Service");

        // Cancel watchers
        _watcherCts?.Cancel();
        
        // Dispose watchers and timers
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();

        foreach (var timer in _debounceTimers.Values)
        {
            timer.Stop();
            timer.Dispose();
        }
        _debounceTimers.Clear();

        _watcherCts?.Dispose();

        await Task.CompletedTask;
    }

    private async Task InitializeSkillsAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Initializing skills from {Count} directories", _skillDirectories.Length);

            // Seed runtime registry with configured directories before loading dynamic tools.
            await _skillRegistry.InitializeAsync(_skillDirectories);
            
            // Initialize the plugin host (synchronously load all skills)
            await _pluginHost.InitializeAsync();
            
            _logger.LogInformation("Skills initialized successfully");
            
            // Notify listeners of loaded skills
            var skills = await _skillRegistry.GetAllSkillsAsync();
            foreach (var skillName in skills.Keys)
            {
                var skill = skills[skillName];
                if (skill.IsAvailable)
                {
                    await NotifySkillAvailableAsync(skillName, skill, ct);
                }
                else
                {
                    await NotifySkillUnavailableAsync(skillName, skill.UnavailableReason ?? "Unknown reason", ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize skills");
            throw;
        }
    }

    private void SetupFileWatcher(string directory, CancellationToken ct)
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Skill directory does not exist: {Directory}", directory);
            return;
        }

        var watcher = new FileSystemWatcher(directory)
        {
            Filter = "SKILL.md",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };

        watcher.Changed += (s, e) => OnSkillFileChanged(directory, ct);
        watcher.Created += (s, e) => OnSkillFileChanged(directory, ct);
        watcher.Deleted += (s, e) => OnSkillFileChanged(directory, ct);
        watcher.Renamed += (s, e) => OnSkillFileChanged(directory, ct);

        watcher.EnableRaisingEvents = true;
        _watchers[directory] = watcher;

        _logger.LogInformation("Watching skill directory: {Directory}", directory);
    }

    private void OnSkillFileChanged(string directory, CancellationToken ct)
    {
        // Debounce rapid file changes
        string debounceKey = $"watcher:{directory}";

        if (_debounceTimers.TryGetValue(debounceKey, out var existingTimer))
        {
            existingTimer.Stop();
            existingTimer.Dispose();
            _debounceTimers.Remove(debounceKey);
        }

        var timer = new System.Timers.Timer(DEBOUNCE_DELAY_MS)
        {
            AutoReset = false
        };

        timer.Elapsed += async (s, e) =>
        {
            try
            {
                _logger.LogInformation("Skill file changed in {Directory}, refreshing", directory);
                await HandleSkillRefreshAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling skill file change");
            }
            finally
            {
                timer.Dispose();
                _debounceTimers.Remove(debounceKey);
            }
        };

        _debounceTimers[debounceKey] = timer;
        timer.Start();
    }

    private async Task HandleSkillRefreshAsync(CancellationToken ct)
    {
        try
        {
            // Refresh the skill registry cache
            await _skillRegistry.RefreshSkillsAsync();

            // Reinitialize the plugin host with updated skills
            await _pluginHost.RefreshAsync();

            var skills = await _skillRegistry.GetAllSkillsAsync();
            var skillNames = skills.Keys.ToList();
            
            _logger.LogInformation("Skills reloaded: {Count} available", skills.Values.Count(s => s.IsAvailable));
            
            // Notify listeners of reload
            await NotifySkillsReloadedAsync(skillNames, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh skills");
        }
    }

    private async Task NotifySkillAvailableAsync(string skillName, SkillDefinition skill, CancellationToken ct)
    {
        foreach (var listener in _lifecycleListeners)
        {
            try
            {
                await listener.OnSkillAvailableAsync(skillName, skill, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lifecycle listener failed on skill available: {SkillName}", skillName);
            }
        }
    }

    private async Task NotifySkillUnavailableAsync(string skillName, string reason, CancellationToken ct)
    {
        foreach (var listener in _lifecycleListeners)
        {
            try
            {
                await listener.OnSkillUnavailableAsync(skillName, reason, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lifecycle listener failed on skill unavailable: {SkillName}", skillName);
            }
        }
    }

    private async Task NotifySkillsReloadedAsync(IReadOnlyList<string> skillNames, CancellationToken ct)
    {
        foreach (var listener in _lifecycleListeners)
        {
            try
            {
                await listener.OnSkillsReloadedAsync(skillNames, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lifecycle listener failed on skills reloaded");
            }
        }
    }
}
