using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Data.Interceptors;
using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Data;

/// <summary>
/// Covers the EF Core save interceptors used by the data layer.
/// </summary>
public class InterceptorTests
{
    /// <summary>
    /// Creates a permit with a stable test badge.
    /// </summary>
    private static IPermit CreateStubPermit()
    {
        var mock = new Mock<IPermit>();
        mock.Setup(p => p.Badge).Returns(new Badge
        {
            Id = Guid.NewGuid(),
            FullName = "Test User",
            Email = "test@example.com"
        });
        return mock.Object;
    }

    /// <summary>
    /// Creates an isolated entity context with the supplied interceptors.
    /// </summary>
    private static EntityContext CreateContext(ISaveChangesInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptors)
            .Options;
        return new EntityContext(options);
    }

    /// <summary>
    /// Verifies the auditable interceptor stamps creation metadata on insert.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task AuditableInterceptor_SetsCreatedOnAndCreatedBy_OnAdd()
    {
        var permit = CreateStubPermit();
        var interceptors = new ISaveChangesInterceptor[] { new AuditableInterceptor(permit) };
        using var ctx = CreateContext(interceptors);

        var tenant = new TenantEntity
        {
            Name = "Test Tenant",
            HostName = "test.example.com",
            IsActive = true,
            CreatedOn = default,
            CreatedBy = new Badge()
        };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        tenant.CreatedOn.Should().NotBe(default);
        tenant.CreatedBy.Should().NotBeNull();
        tenant.CreatedBy.FullName.Should().Be("Test User");
    }

    /// <summary>
    /// Verifies the auditable interceptor stamps update metadata on modification.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task AuditableInterceptor_SetsUpdatedOnAndUpdated_OnModify()
    {
        var permit = CreateStubPermit();
        var interceptors = new ISaveChangesInterceptor[] { new AuditableInterceptor(permit) };
        using var ctx = CreateContext(interceptors);

        var tenant = new TenantEntity
        {
            Name = "Test Tenant",
            HostName = "test.example.com",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.NewGuid(), FullName = "Creator", Email = "c@e.com" }
        };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        tenant.Name = "Updated Tenant";
        await ctx.SaveChangesAsync();

        tenant.UpdatedOn.Should().NotBeNull();
        tenant.UpdatedBy.Should().NotBeNull();
        tenant.UpdatedBy!.FullName.Should().Be("Test User");
    }

    /// <summary>
    /// Verifies the recyclable interceptor soft-deletes tracked entities.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task RecyclableInterceptor_SoftDeletes_OnDelete()
    {
        var permit = CreateStubPermit();
        var interceptors = new ISaveChangesInterceptor[] { new RecyclableInterceptor(permit) };
        using var ctx = CreateContext(interceptors);

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            UserName = "testuser",
            FullName = "Test User",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.NewGuid(), FullName = "Creator", Email = "c@e.com" }
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.Users.Remove(user);
        await ctx.SaveChangesAsync();

        user.IsDeleted.Should().BeTrue();
        var fromDb = await ctx.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == user.Id);
        fromDb.Should().NotBeNull();
        fromDb!.IsDeleted.Should().BeTrue();
    }
}