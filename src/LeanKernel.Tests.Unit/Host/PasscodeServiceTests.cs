using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Tests.Unit.Host;

public sealed class PasscodeServiceTests
{
    [Fact]
    public void HashPasscode_ProducesDeterministicFormat()
    {
        var hash = PasscodeService.HashPasscode("test-passcode");
        var parts = hash.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Equal("200000", parts[0]);
        Assert.NotEmpty(parts[1]); // salt
        Assert.NotEmpty(parts[2]); // hash
    }

    [Fact]
    public void VerifyHash_CorrectPasscode_ReturnsTrue()
    {
        var hash = PasscodeService.HashPasscode("my-secret-code");
        Assert.True(PasscodeService.VerifyHash("my-secret-code", hash));
    }

    [Fact]
    public void VerifyHash_WrongPasscode_ReturnsFalse()
    {
        var hash = PasscodeService.HashPasscode("correct-code");
        Assert.False(PasscodeService.VerifyHash("wrong-code", hash));
    }

    [Fact]
    public void VerifyHash_InvalidFormat_ReturnsFalse()
    {
        Assert.False(PasscodeService.VerifyHash("anything", "not-a-valid-hash"));
        Assert.False(PasscodeService.VerifyHash("anything", ""));
        Assert.False(PasscodeService.VerifyHash("anything", "abc.def"));
    }

    [Fact]
    public void HashPasscode_DifferentInputs_ProduceDifferentHashes()
    {
        var hash1 = PasscodeService.HashPasscode("pass1");
        var hash2 = PasscodeService.HashPasscode("pass2");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashPasscode_SameInput_ProducesDifferentHashesDueToSalt()
    {
        var hash1 = PasscodeService.HashPasscode("same-pass");
        var hash2 = PasscodeService.HashPasscode("same-pass");
        Assert.NotEqual(hash1, hash2);
        // But both verify correctly
        Assert.True(PasscodeService.VerifyHash("same-pass", hash1));
        Assert.True(PasscodeService.VerifyHash("same-pass", hash2));
    }
}
