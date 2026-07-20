using System.Security.Claims;
using System.Text.RegularExpressions;

using LeanKernel.Entities;

namespace LeanKernel.Gateway;

/// <summary>
/// Provides extension methods for the <see cref="ClaimsPrincipal"/> class.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    private const string _emailRegex = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+(\.[a-zA-Z]{2,})?$";
    private const string _guidRegex = @"^[{(]?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}[)}]?$";
    /// <summary>
    /// Converts a <see cref="ClaimsPrincipal"/> into a <see cref="Badge"/> object.
    /// </summary>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> instance to convert.</param>
    /// <returns>A <see cref="Badge"/> object containing the user's ID, full name, and email.</returns>
    /// <remarks>
    /// This method extracts claims such as SID, Name, and Email from the <see cref="ClaimsPrincipal"/>.
    /// If any required claim is missing, default values are used.
    /// </remarks>
    public static Badge ToBadge(this ClaimsPrincipal principal)
    {
        var id = principal.Id();
        var name = principal.Name();
        var email = principal.Email();
        return new Badge
        {
            Id = id,
            FullName = name,
            Email = email
        };
    }

    /// <summary>
    /// Retrieves the user ID from the claims of the <see cref="ClaimsPrincipal"/>.
    /// </summary>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> instance.</param>
    /// <returns>The user ID as a <see cref="Guid"/>.</returns>
    public static Guid Id(this ClaimsPrincipal principal)
    {
        var id = principal.Claims.Where(x => (
                        x.Type == ClaimTypes.Sid ||
                        x.Type == ClaimTypes.NameIdentifier ||
                        x.Type == "sid" ||
                        x.Type == "sub") &&
                        !string.IsNullOrEmpty(x.Value) &&
                        Regex.IsMatch(x.Value, _guidRegex, RegexOptions.IgnoreCase))
                        .Select(x => Guid.Parse(x.Value))
                        .FirstOrDefault();

        return id;
    }

    /// <summary>
    /// Retrieves the user's full name from the claims of the <see cref="ClaimsPrincipal"/>.
    /// </summary>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> instance.</param>
    /// <returns>The user's full name as a string.</returns>
    public static string Name(this ClaimsPrincipal principal)
    {
        var name = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
                   ?? $"{principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value} {principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Surname)?.Value}"
                   ?? string.Empty;

        return name;
    }

    /// <summary>
    /// Retrieves the user's email from the claims of the <see cref="ClaimsPrincipal"/>.
    /// </summary>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> instance.</param>
    /// <returns>The user's email as a string, or an empty string if not found.</returns>
    public static string Email(this ClaimsPrincipal principal)
    {
        var email = principal.Claims.FirstOrDefault(x => (
                x.Type == ClaimTypes.Email ||
                x.Type == ClaimTypes.Name ||
                x.Type == ClaimTypes.Upn ||
                x.Type == "email" ||
                x.Type == "upn" ||
                x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress") &&
                !string.IsNullOrEmpty(x.Value) &&
                Regex.IsMatch(x.Value, _emailRegex, RegexOptions.IgnoreCase)
                );
        return email?.Value ?? string.Empty;
    }
}