using System.Security.Cryptography;

namespace LeanKernel.Host.Services.Auth;

/// <summary>
/// Passcode verification service using PBKDF2-SHA512 with timing-safe comparison.
/// </summary>
public sealed class PasscodeService : IPasscodeService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 200_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    private readonly IAuthStateStore _store;
    private readonly ISecurityStampService _stampService;
    private readonly ILogger<PasscodeService> _logger;

    public PasscodeService(
        IAuthStateStore store,
        ISecurityStampService stampService,
        ILogger<PasscodeService> logger)
    {
        _store = store;
        _stampService = stampService;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            var state = _store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            return !string.IsNullOrEmpty(state.PasscodeHash);
        }
    }

    public async Task<bool> VerifyAsync(string passcode, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        if (string.IsNullOrEmpty(state.PasscodeHash))
            return false;

        return VerifyHash(passcode, state.PasscodeHash);
    }

    public async Task SetAsync(string newPasscode, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        state.PasscodeHash = HashPasscode(newPasscode);
        await _store.SaveAsync(state, ct);
        await _stampService.RotateStampAsync(ct);
        _logger.LogWarning("Passcode set/changed");
    }

    public async Task ChangeAsync(string currentPasscode, string newPasscode, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        if (string.IsNullOrEmpty(state.PasscodeHash) || !VerifyHash(currentPasscode, state.PasscodeHash))
            throw new UnauthorizedAccessException("Current passcode is incorrect");

        state.PasscodeHash = HashPasscode(newPasscode);
        await _store.SaveAsync(state, ct);
        await _stampService.RotateStampAsync(ct);
        _logger.LogWarning("Passcode changed");
    }

    public static string HashPasscode(string passcode)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(passcode, salt, Iterations, Algorithm, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyHash(string passcode, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[1]);
        var expectedHash = Convert.FromBase64String(parts[2]);

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(passcode, salt, iterations, Algorithm, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
