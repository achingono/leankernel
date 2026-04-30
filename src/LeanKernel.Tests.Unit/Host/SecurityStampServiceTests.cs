using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Tests.Unit.Host;

public sealed class SecurityStampServiceTests
{
    [Fact]
    public async Task GetStampAsync_ReturnsStampFromState()
    {
        var store = new InMemoryAuthStateStore();
        var state = await store.LoadAsync();
        var expectedStamp = state.SecurityStamp;

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityStampService>();
        var service = new SecurityStampService(store, logger);

        var stamp = await service.GetStampAsync();
        Assert.Equal(expectedStamp, stamp);
    }

    [Fact]
    public async Task RotateStampAsync_ChangesStamp()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityStampService>();
        var service = new SecurityStampService(store, logger);

        var originalStamp = await service.GetStampAsync();
        var newStamp = await service.RotateStampAsync();

        Assert.NotEqual(originalStamp, newStamp);
    }

    [Fact]
    public async Task ValidateStampAsync_ValidStamp_ReturnsTrue()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityStampService>();
        var service = new SecurityStampService(store, logger);

        var stamp = await service.GetStampAsync();
        Assert.True(await service.ValidateStampAsync(stamp));
    }

    [Fact]
    public async Task ValidateStampAsync_InvalidStamp_ReturnsFalse()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityStampService>();
        var service = new SecurityStampService(store, logger);

        Assert.False(await service.ValidateStampAsync("wrong-stamp"));
    }

    [Fact]
    public async Task ValidateStampAsync_AfterRotation_OldStampInvalid()
    {
        var store = new InMemoryAuthStateStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityStampService>();
        var service = new SecurityStampService(store, logger);

        var oldStamp = await service.GetStampAsync();
        var newStamp = await service.RotateStampAsync();

        Assert.False(await service.ValidateStampAsync(oldStamp));
        Assert.True(await service.ValidateStampAsync(newStamp));
    }
}
