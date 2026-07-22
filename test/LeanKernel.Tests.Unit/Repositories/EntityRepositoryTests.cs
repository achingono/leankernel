using System.Linq.Expressions;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Interfaces;
using LeanKernel.Logic.Repositories;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Repositories;

/// <summary>
/// Covers <see cref="EntityRepository{TEntity}"/> behavior: filtered reads, write authorization, audit stamping.
/// </summary>
public sealed class EntityRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public EntityRepositoryTests() => _connection.Open();
    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task GetAll_AppliesFilterPredicate()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var (repo, context) = CreateSut<SessionEntity>(tenantId, filterPredicate: e => e.TenantId == tenantId);

        var tenant = new TenantEntity { Id = tenantId, Name = "t1", Description = "d", HostName = $"h-{tenantId:N}", IsActive = true, CreatedOn = DateTime.UtcNow, CreatedBy = new Badge() };
        var otherTenant = new TenantEntity { Id = otherTenantId, Name = "t2", Description = "d", HostName = $"h-{otherTenantId:N}", IsActive = true, CreatedOn = DateTime.UtcNow, CreatedBy = new Badge() };
        var user = new UserEntity { Id = Guid.NewGuid(), Email = "u@t", UserName = "u", FirstName = "f", LastName = "l", FullName = "f l", IsActive = true, CreatedBy = new Badge(), Issuer = "iss", Subject = "sub" };
        var channel = new ChannelEntity { Id = Guid.NewGuid(), Name = "ch" };
        context.Tenants.AddRange(tenant, otherTenant);
        context.Users.Add(user);
        context.Channels.Add(channel);

        context.Sessions.Add(new SessionEntity { Id = Guid.NewGuid(), TenantId = tenantId, UserId = user.Id, ChannelId = channel.Id, Tenant = null!, User = null!, Channel = null!, CreatedBy = new Badge() });
        context.Sessions.Add(new SessionEntity { Id = Guid.NewGuid(), TenantId = otherTenantId, UserId = user.Id, ChannelId = channel.Id, Tenant = null!, User = null!, Channel = null!, CreatedBy = new Badge() });
        await context.SaveChangesAsync();

        var results = await repo.GetAll().ToListAsync();

        results.Should().ContainSingle();
        results.Single().TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForOutOfScope()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var (repo, context) = CreateSut<SessionEntity>(tenantId, filterPredicate: e => e.TenantId == tenantId);

        var otherTenant = new TenantEntity { Id = otherTenantId, Name = "t2", Description = "d", HostName = $"h-{otherTenantId:N}", IsActive = true, CreatedOn = DateTime.UtcNow, CreatedBy = new Badge() };
        var user = new UserEntity { Id = Guid.NewGuid(), Email = "u@t", UserName = "u", FirstName = "f", LastName = "l", FullName = "f l", IsActive = true, CreatedBy = new Badge(), Issuer = "iss", Subject = "sub" };
        var channel = new ChannelEntity { Id = Guid.NewGuid(), Name = "ch" };
        context.Tenants.Add(otherTenant);
        context.Users.Add(user);
        context.Channels.Add(channel);

        var entity = new SessionEntity { Id = Guid.NewGuid(), TenantId = otherTenantId, UserId = user.Id, ChannelId = channel.Id, Tenant = null!, User = null!, Channel = null!, CreatedBy = new Badge() };
        context.Sessions.Add(entity);
        await context.SaveChangesAsync();

        var result = await repo.GetByIdAsync(entity.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Add_WhenCreateDenied_Throws()
    {
        var permitMock = new Mock<IPermit<SessionEntity>>();
        permitMock.Setup(p => p.Can(Operation.Create)).Returns(false);

        var filterMock = new Mock<IFilter<SessionEntity>>();
        filterMock.Setup(f => f.Predicate).Returns((Expression<Func<SessionEntity, bool>>?)null);

        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new EntityContext(options);
        await context.Database.EnsureCreatedAsync();

        var repo = new EntityRepository<SessionEntity>(context, filterMock.Object, permitMock.Object);

        var act = () => repo.Add(new SessionEntity { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), UserId = Guid.NewGuid(), ChannelId = Guid.NewGuid(), Tenant = null!, User = null!, Channel = null!, CreatedBy = new Badge() });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Create not permitted*");
    }

    [Fact]
    public async Task Add_WhenCreatePermitted_StampsAuditFields()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var permitMock = new Mock<IPermit<SessionEntity>>();
        permitMock.Setup(p => p.Can(Operation.Create)).Returns(true);
        permitMock.Setup(p => p.UserId).Returns(userId);
        permitMock.Setup(p => p.TenantId).Returns(tenantId);
        permitMock.Setup(p => p.ChannelId).Returns(channelId);
        permitMock.Setup(p => p.Badge).Returns(new Badge { Id = userId, FullName = "Test", Email = "test@test" });

        var filterMock = new Mock<IFilter<SessionEntity>>();
        filterMock.Setup(f => f.Predicate).Returns((Expression<Func<SessionEntity, bool>>?)null);

        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new EntityContext(options);
        await context.Database.EnsureCreatedAsync();

        var repo = new EntityRepository<SessionEntity>(context, filterMock.Object, permitMock.Object);

        var entity = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Empty,
            UserId = Guid.Empty,
            ChannelId = Guid.Empty,
            Tenant = null!,
            User = null!,
            Channel = null!,
            CreatedBy = new Badge(),
        };

        repo.Add(entity);

        entity.CreatedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        entity.CreatedBy.Id.Should().Be(userId);
        entity.TenantId.Should().Be(tenantId);
        entity.UserId.Should().Be(userId);
        entity.ChannelId.Should().Be(channelId);
    }

    [Fact]
    public async Task Update_WhenUpdateDenied_Throws()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var permitMock = new Mock<IPermit<SessionEntity>>();
        permitMock.Setup(p => p.Can(Operation.Update)).Returns(false);

        var filterMock = new Mock<IFilter<SessionEntity>>();
        filterMock.Setup(f => f.Predicate).Returns((Expression<Func<SessionEntity, bool>>?)null);

        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new EntityContext(options);
        await context.Database.EnsureCreatedAsync();

        SeedIdentityContext(context, tenantId, userId, channelId);

        var repo = new EntityRepository<SessionEntity>(context, filterMock.Object, permitMock.Object);

        var entity = new SessionEntity { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, ChannelId = channelId, Tenant = null!, User = null!, Channel = null!, CreatedBy = new Badge() };
        context.Sessions.Add(entity);
        await context.SaveChangesAsync();

        var act = () => repo.Update(entity);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Update not permitted*");
    }

    [Fact]
    public async Task Delete_WhenDeleteDenied_Throws()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var permitMock = new Mock<IPermit<SessionEntity>>();
        permitMock.Setup(p => p.Can(Operation.Delete)).Returns(false);

        var filterMock = new Mock<IFilter<SessionEntity>>();
        filterMock.Setup(f => f.Predicate).Returns((Expression<Func<SessionEntity, bool>>?)null);

        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new EntityContext(options);
        await context.Database.EnsureCreatedAsync();

        SeedIdentityContext(context, tenantId, userId, channelId);

        var repo = new EntityRepository<SessionEntity>(context, filterMock.Object, permitMock.Object);

        var entity = new SessionEntity { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, ChannelId = channelId, Tenant = null!, User = null!, Channel = null!, CreatedBy = new Badge() };
        context.Sessions.Add(entity);
        await context.SaveChangesAsync();

        var act = () => repo.Delete(entity);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Delete not permitted*");
    }

    private static void SeedIdentityContext(EntityContext context, Guid tenantId, Guid userId, Guid channelId)
    {
        context.Tenants.Add(new TenantEntity { Id = tenantId, Name = "t", Description = "d", HostName = $"h-{tenantId:N}", IsActive = true, CreatedOn = DateTime.UtcNow, CreatedBy = new Badge() });
        context.Users.Add(new UserEntity { Id = userId, Email = "u@t", UserName = "u", FirstName = "f", LastName = "l", FullName = "f l", IsActive = true, CreatedBy = new Badge(), Issuer = "iss", Subject = "sub" });
        context.Channels.Add(new ChannelEntity { Id = channelId, Name = "ch" });
    }

    private (IRepository<TEntity> repo, EntityContext context) CreateSut<TEntity>(
        Guid? tenantId = null,
        Expression<Func<TEntity, bool>>? filterPredicate = null)
        where TEntity : class, IEntity
    {
        var permitMock = new Mock<IPermit<TEntity>>();
        permitMock.Setup(p => p.Can(It.IsAny<Operation>())).Returns(true);
        if (tenantId.HasValue)
        {
            permitMock.Setup(p => p.TenantId).Returns(tenantId.Value);
        }

        var filterMock = new Mock<IFilter<TEntity>>();
        filterMock.Setup(f => f.Predicate).Returns(filterPredicate);

        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new EntityContext(options);
        context.Database.EnsureCreated();

        return (new EntityRepository<TEntity>(context, filterMock.Object, permitMock.Object), context);
    }
}