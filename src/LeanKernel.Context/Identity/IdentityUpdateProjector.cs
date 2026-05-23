using System.Text.RegularExpressions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.Identity;

/// <summary>
/// Projects allowlisted identity updates from assistant responses back into GBrain.
/// </summary>
public sealed partial class IdentityUpdateProjector
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly IdentityConfig _config;
    private readonly IDiagnosticsSink? _diagnosticsSink;
    private readonly ILogger<IdentityUpdateProjector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityUpdateProjector"/> class.
    /// </summary>
    /// <param name="knowledgeService">The knowledge service.</param>
    /// <param name="config">The identity configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="diagnosticsSink">The optional diagnostics sink.</param>
    public IdentityUpdateProjector(
        IKnowledgeService knowledgeService,
        IOptions<IdentityConfig> config,
        ILogger<IdentityUpdateProjector> logger,
        IDiagnosticsSink? diagnosticsSink = null)
    {
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _diagnosticsSink = diagnosticsSink;
    }

    /// <inheritdoc />
    public async Task<string> EnhanceAsync(string response, ConversationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(context);

        if (!_config.EnableIdentityExtraction)
        {
            return response;
        }

        var extractedUpdates = ExtractUpdates(response)
            .Where(update => _config.AllowedIdentityFields.Contains(update.FieldName, StringComparer.OrdinalIgnoreCase))
            .GroupBy(static update => update.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .ToList();

        if (extractedUpdates.Count == 0)
        {
            return response;
        }

        try
        {
            await PersistUpdatesAsync(extractedUpdates, context, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Identity writeback failed for session {SessionId}", context.SessionId);
        }

        return response;
    }

    private async Task PersistUpdatesAsync(
        IReadOnlyList<ExtractedIdentityUpdate> updates,
        ConversationContext context,
        CancellationToken ct)
    {
        var existingPage = await _knowledgeService.GetPageAsync(_config.UserPreferencePageKey, ct).ConfigureAwait(false);
        var document = IdentityPageSerializer.ParseDocument(existingPage?.Content, _logger);
        var metadata = new Dictionary<string, string>(document.Metadata, StringComparer.OrdinalIgnoreCase);
        var fields = new Dictionary<string, IdentityField>(document.Fields, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        metadata.TryAdd("id", _config.UserPreferencePageKey);
        metadata.TryAdd("pageType", "identity-user-preferences");
        metadata.TryAdd("subject", context.Identity?.UserId ?? "primary-user");
        metadata.TryAdd("scope", "private");
        metadata.TryAdd("sourceOfTruth", "gbrain");

        foreach (var update in updates)
        {
            if (fields.TryGetValue(update.FieldName, out var existingField))
            {
                if (string.Equals(existingField.Value.Trim(), update.Value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var refreshedField = existingField with
                    {
                        Confidence = Math.Max(existingField.Confidence, update.Confidence),
                        LastUpdated = update.LastUpdated,
                        Source = update.Source,
                    };

                    if (!EqualityComparer<IdentityField>.Default.Equals(existingField, refreshedField))
                    {
                        fields[update.FieldName] = refreshedField;
                        changed = true;
                    }

                    continue;
                }

                if (existingField.Confidence > update.Confidence)
                {
                    await RecordConflictAsync(context, update, existingField, ct).ConfigureAwait(false);
                    continue;
                }
            }

            fields[update.FieldName] = new IdentityField
            {
                Name = update.FieldName,
                Value = update.Value,
                Confidence = update.Confidence,
                LastUpdated = update.LastUpdated,
                Source = update.Source,
            };
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        var serialized = IdentityPageSerializer.SerializeDocument(metadata, fields, document.Body);
        if (string.Equals(existingPage?.Content, serialized, StringComparison.Ordinal))
        {
            return;
        }

        await _knowledgeService.PutPageAsync(_config.UserPreferencePageKey, serialized, ct).ConfigureAwait(false);
    }

    private async Task RecordConflictAsync(
        ConversationContext context,
        ExtractedIdentityUpdate update,
        IdentityField existingField,
        CancellationToken ct)
    {
        if (_diagnosticsSink is null || string.IsNullOrWhiteSpace(context.SessionId))
        {
            return;
        }

        await _diagnosticsSink.RecordAsync(
            new DiagnosticEntry
            {
                SessionId = context.SessionId,
                Category = DiagnosticCategory.ResponseEnhancement.ToString(),
                Payload = new
                {
                    code = "identity_conflict",
                    field = update.FieldName,
                    proposedValue = update.Value,
                    proposedConfidence = update.Confidence,
                    existingValue = existingField.Value,
                    existingConfidence = existingField.Confidence,
                },
            },
            ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<ExtractedIdentityUpdate> ExtractUpdates(string response)
    {
        var updates = new List<ExtractedIdentityUpdate>();
        AddIfMatched(updates, PreferredNameRegex(), response, "preferred_name", 0.75);
        AddIfMatched(updates, TimezoneRegex(), response, "timezone", 0.75);
        AddIfMatched(updates, LocaleRegex(), response, "locale", 0.75);
        AddIfMatched(updates, CommunicationStyleRegex(), response, "communication_style", 0.7);
        AddIfMatched(updates, WorkStyleRegex(), response, "work_style", 0.7);
        AddIfMatched(updates, RecurringGoalsRegex(), response, "recurring_goals", 0.7);
        AddIfMatched(updates, ToolPreferencesRegex(), response, "tool_preferences", 0.7);
        AddIfMatched(updates, AutonomyLevelRegex(), response, "autonomy_level", 0.7);
        return updates;
    }

    private static void AddIfMatched(
        ICollection<ExtractedIdentityUpdate> updates,
        Regex regex,
        string response,
        string fieldName,
        double confidence)
    {
        var match = regex.Match(response);
        if (!match.Success)
        {
            return;
        }

        var value = match.Groups["value"].Value.Trim().TrimEnd('.', '!', '?', ';', ',');
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        updates.Add(new ExtractedIdentityUpdate(
            fieldName,
            value,
            confidence,
            DateTimeOffset.UtcNow,
            "assistant_response"));
    }

    [GeneratedRegex(@"\b(?:call you|preferred name is|you go by)\s+(?<value>[A-Za-z][A-Za-z0-9' -]{0,39})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreferredNameRegex();

    [GeneratedRegex(@"\b(?:timezone(?: is| to| as)?|use)\s+(?<value>UTC[+-]\d{1,2}(?::\d{2})?|[A-Za-z_]+/[A-Za-z_]+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimezoneRegex();

    [GeneratedRegex(@"\blocale(?: is| to| as)?\s+(?<value>[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocaleRegex();

    [GeneratedRegex(@"\bcommunication style(?: is| to be| as)?\s+(?<value>[^.\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommunicationStyleRegex();

    [GeneratedRegex(@"\bwork style(?: is| to be| as)?\s+(?<value>[^.\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WorkStyleRegex();

    [GeneratedRegex(@"\brecurring goals?(?: are| include| to be)?\s+(?<value>[^.\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RecurringGoalsRegex();

    [GeneratedRegex(@"\btool preferences?(?: are| include| to be)?\s+(?<value>[^.\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolPreferencesRegex();

    [GeneratedRegex(@"\bautonomy level(?: is| to be| as)?\s+(?<value>[^.\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AutonomyLevelRegex();

    private sealed record ExtractedIdentityUpdate(
        string FieldName,
        string Value,
        double Confidence,
        DateTimeOffset LastUpdated,
        string Source);
}
