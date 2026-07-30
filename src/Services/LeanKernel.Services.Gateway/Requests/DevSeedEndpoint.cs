namespace LeanKernel.Services.Gateway.Requests;

using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Maps a development-only endpoint for seeding channel sender bindings.
/// </summary>
public static class DevSeedEndpoint
{
    /// <summary>
    /// Registers the development-only seed endpoint.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    public static void MapDevSeedEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/dev/seed", HandleSeedBindingAsync);
    }

    /// <summary>
    /// Creates or updates a channel sender binding using existing tenant, channel, and user rows.
    /// </summary>
    private static async Task<IResult> HandleSeedBindingAsync(
        HttpContext context,
        [FromBody] SeedBindingRequest request,
        [FromServices] EntityContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(request.ChannelName)
            || string.IsNullOrWhiteSpace(request.Issuer)
            || string.IsNullOrWhiteSpace(request.Subject)
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.FirstName)
            || string.IsNullOrWhiteSpace(request.LastName))
        {
            return Results.BadRequest(new { error = "channel_name, issuer, subject, email, first_name, and last_name are required." });
        }

        if (!System.Net.Mail.MailAddress.TryCreate(request.Email, out _))
        {
            return Results.BadRequest(new { error = "email is invalid." });
        }

        var tenantHostName = context.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(tenantHostName))
        {
            return Results.BadRequest(new { error = "request host is required." });
        }

        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(tenant => tenant.HostName == tenantHostName);
        var channel = await dbContext.Channels.FirstOrDefaultAsync(channel => channel.Name == request.ChannelName);

        if (tenant is null || channel is null)
        {
            return Results.NotFound(new { error = "tenant or channel not found." });
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(user =>
            user.Issuer == request.Issuer
            && user.Subject == request.Subject
            && !user.IsDeleted);
        if (user is null)
        {
            user = new UserEntity
            {
                Id = Guid.NewGuid(),
                Issuer = request.Issuer,
                Subject = request.Subject,
                UserName = request.UserName ?? request.Email.Split('@')[0],
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                FullName = string.IsNullOrEmpty(request.FullName) ? $"{request.FirstName} {request.LastName}" : request.FullName,
                IsActive = true,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge
                {
                    Id = Guid.Empty,
                    FullName = "System",
                    Email = "system@leankernel.local",
                },
                PersonId = Guid.NewGuid(),
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        var binding = await dbContext.ChannelSenderBindings.FirstOrDefaultAsync(binding =>
            binding.TenantId == tenant.Id
            && binding.ChannelId == channel.Id
            && binding.Issuer == request.Issuer
            && binding.Subject == request.Subject);

        if (binding is null)
        {
            binding = new ChannelSenderBindingEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = user.Id,
                ChannelId = channel.Id,
                Issuer = request.Issuer,
                Subject = request.Subject,
                BearerToken = request.BearerToken ?? string.Empty,
                IsActive = true,
                CreatedOn = DateTime.UtcNow,
            };
            dbContext.ChannelSenderBindings.Add(binding);
        }
        else
        {
            binding.UserId = user.Id;
            binding.BearerToken = request.BearerToken ?? string.Empty;
            binding.IsActive = true;
        }

        await dbContext.SaveChangesAsync();
        return Results.Ok(new { binding.Id, binding.TenantId, binding.UserId, binding.ChannelId, binding.Issuer, binding.Subject, binding.BearerToken, binding.IsActive });
    }

    /// <summary>
    /// Request body for seed binding creation.
    /// </summary>
    public sealed record SeedBindingRequest(
        string ChannelName,
        string Issuer,
        string Subject,
        string? UserName,
        string Email,
        string FirstName,
        string LastName,
        string? FullName,
        string? BearerToken);
}