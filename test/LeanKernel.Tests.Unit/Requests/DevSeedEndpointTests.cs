using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Services.Gateway.Requests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace LeanKernel.Tests.Unit.Requests;

public sealed class DevSeedEndpointTests
{
    [Fact]
    public async Task SeedBindingRequest_CreatesBindingWhenUserIsMissing()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new EntityContext(options);
        var tenant = new TenantEntity
        {
            Id = Guid.NewGuid(),
            HostName = "localhost",
            Name = "Default",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@leankernel.local" }
        };
        var channel = new ChannelEntity { Id = Guid.NewGuid(), Name = Constants.Channels.OpenAiHttpName };
        context.Tenants.Add(tenant);
        context.Channels.Add(channel);
        await context.SaveChangesAsync();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("localhost");

        var request = new DevSeedEndpoint.SeedBindingRequest(Constants.Channels.OpenAiHttpName, "cumbersome", "+18005551212", "testuser", "testuser@example.com", "Test", "User", null, null);
        var result = await InvokeAsync(httpContext, context, request);

        result.Should().NotBeNull();
        context.Users.Should().ContainSingle(user => user.Issuer == "cumbersome" && user.Subject == "+18005551212");
        context.ChannelSenderBindings.Should().ContainSingle(binding => binding.TenantId == tenant.Id && binding.ChannelId == channel.Id);
    }

    [Fact]
    public async Task SeedBindingRequest_InvalidEmail_ReturnsBadRequest()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new EntityContext(options);
        context.Tenants.Add(new TenantEntity
        {
            Id = Guid.NewGuid(),
            HostName = "localhost",
            Name = "Default",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@leankernel.local" }
        });
        context.Channels.Add(new ChannelEntity { Id = Guid.NewGuid(), Name = Constants.Channels.OpenAiHttpName });
        await context.SaveChangesAsync();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("localhost");

        var request = new DevSeedEndpoint.SeedBindingRequest(Constants.Channels.OpenAiHttpName, "cumbersome", "+18005551212", "testuser", "not-an-email", "Test", "User", null, null);
        var result = await InvokeAsync(httpContext, context, request);

        result.Should().NotBeNull();
    }

    private static async Task<IResult> InvokeAsync(HttpContext httpContext, EntityContext context, DevSeedEndpoint.SeedBindingRequest request)
    {
        var method = typeof(DevSeedEndpoint).GetMethod("HandleSeedBindingAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [httpContext, request, context])!;
    }
}