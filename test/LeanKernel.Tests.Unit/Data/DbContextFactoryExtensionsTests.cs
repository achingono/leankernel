using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Tests.Unit.TestDoubles;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace LeanKernel.Tests.Unit.Data;

public class DbContextFactoryExtensionsTests
{
    [Fact]
    public async Task ResolveSenderAsync_WithMissingInputs_ReturnsEmpty()
    {
        var factory = CreateFactory();

        var result = await factory.ResolveSenderAsync(string.Empty, "issuer", "openai-http", CancellationToken.None);

        result.Token.Should().BeEmpty();
        result.MatchCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveSenderAsync_WithSingleActiveMatch_ReturnsTokenAndCount()
    {
        var options = NewOptions();
        await using (var context = new EntityContext(options))
        {
            var channel = new ChannelEntity { Id = Guid.NewGuid(), Name = "openai-http" };
            context.Channels.Add(channel);
            context.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                ChannelId = channel.Id,
                Channel = channel,
                Tenant = null!,
                User = null!,
                Issuer = "issuer-a",
                Subject = "subject-a",
                BearerToken = "token-a",
                IsActive = true,
            });

            await context.SaveChangesAsync();
        }

        var factory = new TestDbContextFactory(options);
        var result = await factory.ResolveSenderAsync("subject-a", "issuer-a", "openai-http", CancellationToken.None);

        result.Token.Should().Be("token-a");
        result.MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task ResolveSenderAsync_WithMultipleMatches_ReturnsFirstAndCountTwo()
    {
        var options = NewOptions();
        await using (var context = new EntityContext(options))
        {
            var channel = new ChannelEntity { Id = Guid.NewGuid(), Name = "openai-http" };
            context.Channels.Add(channel);

            context.ChannelSenderBindings.AddRange(
                new ChannelSenderBindingEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = Guid.NewGuid(),
                    UserId = Guid.NewGuid(),
                    ChannelId = channel.Id,
                    Channel = channel,
                    Tenant = null!,
                    User = null!,
                    Issuer = "issuer-b",
                    Subject = "subject-b",
                    BearerToken = "token-b1",
                    IsActive = true,
                },
                new ChannelSenderBindingEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = Guid.NewGuid(),
                    UserId = Guid.NewGuid(),
                    ChannelId = channel.Id,
                    Channel = channel,
                    Tenant = null!,
                    User = null!,
                    Issuer = "issuer-b",
                    Subject = "subject-b",
                    BearerToken = "token-b2",
                    IsActive = true,
                });

            await context.SaveChangesAsync();
        }

        var factory = new TestDbContextFactory(options);
        var result = await factory.ResolveSenderAsync("subject-b", "issuer-b", "openai-http", CancellationToken.None);

        result.Token.Should().NotBeEmpty();
        result.MatchCount.Should().Be(2);
    }

    private static TestDbContextFactory CreateFactory()
    {
        return new TestDbContextFactory(NewOptions());
    }

    private static DbContextOptions<EntityContext> NewOptions()
    {
        return new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }
}
