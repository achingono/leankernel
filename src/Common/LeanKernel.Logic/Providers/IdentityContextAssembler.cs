using System.Text.Json;

using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Renders a deterministic identity profile block for prompt context injection.
/// </summary>
public sealed class IdentityContextAssembler(IOptions<IdentityClaimsContextSettings> settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IdentityClaimsContextSettings _settings = settings.Value;

    /// <summary>
    /// Renders an allowlisted identity context block for the supplied user.
    /// </summary>
    public string? Build(UserEntity? user)
    {
        if (!_settings.Enabled || user is null)
        {
            return null;
        }

        var lines = new List<string>();

        foreach (var field in _settings.PromptFields)
        {
            switch (field)
            {
                case Constants.IdentityContextFields.FullName:
                    AddScalar(lines, Constants.IdentityContextFields.FullName, user.FullName);
                    break;
                case Constants.IdentityContextFields.Email:
                    AddScalar(lines, Constants.IdentityContextFields.Email, user.Email);
                    break;
                case Constants.IdentityContextFields.PreferredUsername:
                    AddScalar(lines, Constants.IdentityContextFields.PreferredUsername, user.PreferredUserName);
                    break;
                case Constants.IdentityContextFields.Locale:
                    AddScalar(lines, Constants.IdentityContextFields.Locale, user.Locale);
                    break;
                case Constants.IdentityContextFields.TimeZone:
                    AddScalar(lines, Constants.IdentityContextFields.TimeZone, user.TimeZone);
                    break;
                case Constants.IdentityContextFields.Organization:
                    AddScalar(lines, Constants.IdentityContextFields.Organization, user.Organization);
                    break;
                case Constants.IdentityContextFields.Roles:
                    AddList(lines, Constants.IdentityContextFields.Roles, DeserializeList(user.RolesJson));
                    break;
                case Constants.IdentityContextFields.Groups:
                    AddList(lines, Constants.IdentityContextFields.Groups, DeserializeList(user.GroupsJson));
                    break;
                case Constants.IdentityContextFields.CustomClaims:
                    AddCustomClaims(lines, user.CustomClaimsJson);
                    break;
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var block = "Identity profile (allowlisted):\n" + string.Join("\n", lines);
        var estimatedTokens = Math.Max(1, block.Length / 4);
        if (estimatedTokens <= _settings.MaxPromptTokens)
        {
            return block;
        }

        var maxChars = Math.Max(1, _settings.MaxPromptTokens * 4);
        return block.Length <= maxChars ? block : block[..maxChars];
    }

    private static void AddScalar(ICollection<string> lines, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"- {key}: {value.Trim()}");
        }
    }

    private static void AddList(ICollection<string> lines, string key, IReadOnlyList<string> values)
    {
        if (values.Count > 0)
        {
            lines.Add($"- {key}: {string.Join(", ", values)}");
        }
    }

    private static void AddCustomClaims(ICollection<string> lines, string? customClaimsJson)
    {
        if (string.IsNullOrWhiteSpace(customClaimsJson))
        {
            return;
        }

        Dictionary<string, List<string>>? map;
        try
        {
            map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(customClaimsJson, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (map is null || map.Count == 0)
        {
            return;
        }

        foreach (var pair in map.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var values = pair.Value
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToList();

            if (values.Count > 0)
            {
                lines.Add($"- custom.{pair.Key}: {string.Join(", ", values)}");
            }
        }
    }

    private static IReadOnlyList<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
            return values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
