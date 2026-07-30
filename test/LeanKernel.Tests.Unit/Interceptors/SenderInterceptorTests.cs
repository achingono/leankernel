using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Data.Interceptors;
using LeanKernel.Entities;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Interceptors;

public sealed class SenderInterceptorTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private DbContextOptions<EntityContext> _entityContextOptions = null!;

    public SenderInterceptorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public async Task InitializeAsync()
    {
        _entityContextOptions = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;

        await using var ctx = new EntityContext(_entityContextOptions);
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SavingChangesAsync_GeneratesTokenForBindingWithoutBearerToken()
    {
        var generator = new Mock<ISecurityTokenGenerator>();
        generator.Setup(g => g.GenerateToken(It.IsAny<ChannelSenderBindingEntity>(), true))
            .Returns("test-token");

        var interceptor = new SenderInterceptor(generator.Object);

        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;

        await using (var ctx = new EntityContext(options))
        {
            var user = new UserEntity
            {
                Id = Guid.NewGuid(),
                Email = "test@test.com",
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.NewGuid(), Email = "admin@test.com", FullName = "Admin" },
            };
            var tenant = new TenantEntity
            {
                Id = Guid.NewGuid(),
                Name = "Test Tenant",
                HostName = "test",
                IsActive = true,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.NewGuid(), Email = "admin@test.com", FullName = "Admin" },
            };
            var channel = new ChannelEntity { Id = Guid.NewGuid(), Name = "test-channel" };
            ctx.Users.Add(user);
            ctx.Tenants.Add(tenant);
            ctx.Channels.Add(channel);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new EntityContext(options))
        {
            var user = await ctx.Users.FirstAsync();
            var tenant = await ctx.Tenants.FirstAsync();
            var channel = await ctx.Channels.FirstAsync();

            var binding = new ChannelSenderBindingEntity
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TenantId = tenant.Id,
                ChannelId = channel.Id,
                Issuer = "issuer",
                Subject = "subject",
            };
            ctx.ChannelSenderBindings.Add(binding);
            await ctx.SaveChangesAsync();
        }

        generator.Verify(g => g.GenerateToken(It.IsAny<ChannelSenderBindingEntity>(), true), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SavingChangesAsync_SkipsBindingWithExistingBearerToken()
    {
        var generator = new Mock<ISecurityTokenGenerator>();

        var interceptor = new SenderInterceptor(generator.Object);

        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;

        var adminBadge = new Badge { Id = Guid.NewGuid(), Email = "admin@test.com", FullName = "Admin" };

        await using var ctx = new EntityContext(options);
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = "u@t.com",
            CreatedOn = DateTime.UtcNow,
            CreatedBy = adminBadge,
        };
        var tenant = new TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = "T",
            HostName = "h",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = adminBadge,
        };
        var channel = new ChannelEntity { Id = Guid.NewGuid(), Name = "c" };
        ctx.Users.Add(user);
        ctx.Tenants.Add(tenant);
        ctx.Channels.Add(channel);
        await ctx.SaveChangesAsync();

        var binding = new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenant.Id,
            ChannelId = channel.Id,
            BearerToken = "existing-token",
        };
        ctx.ChannelSenderBindings.Add(binding);
        await ctx.SaveChangesAsync();

        generator.Verify(g => g.GenerateToken(It.IsAny<ChannelSenderBindingEntity>(), true), Times.Never);
    }
}
