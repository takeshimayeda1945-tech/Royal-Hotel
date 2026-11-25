using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Intouch_67173986_L003.Models;

public enum PasswordCheck
{
    Failed,
    Success,
    /// <summary>Correct password, but the stored value is old (plain text or weaker settings): save a new hash.</summary>
    SuccessRehashNeeded
}

/// <summary>
/// Passwords are never stored: Users.Password holds a salted PBKDF2 hash made by ASP.NET Core
/// Identity's PasswordHasher (format "AQAAAA…", about 84 characters). Rows created before hashing
/// was added still hold the plain password; they are accepted once and replaced with a hash
/// (Data/DbInitializer.cs also converts them when the site starts).
/// </summary>
public static class PasswordSecurity
{
    private static readonly PasswordHasher<CyberUser> Hasher = new();

    // Used when the e-mail does not exist, so that case takes as long as a wrong password.
    private static readonly Lazy<string> DummyHash = new(() => Hash(Guid.NewGuid().ToString("N")));

    public static string Hash(string password)
    {
        return Hasher.HashPassword(null!, password);
    }

    /// <summary>True when the stored value is a PasswordHasher hash rather than a plain password.</summary>
    public static bool IsHash(string? stored)
    {
        // Version 3 hash: 1 marker byte (0x01) + settings + 16-byte salt + 32-byte key = 61+ bytes in Base64.
        if (string.IsNullOrEmpty(stored) || stored.Length < 80)
        {
            return false;
        }

        var bytes = new byte[stored.Length];
        return Convert.TryFromBase64String(stored, bytes, out var length) && length >= 61 && bytes[0] == 0x01;
    }

    public static PasswordCheck Verify(string? stored, string password)
    {
        if (IsHash(stored))
        {
            return Hasher.VerifyHashedPassword(null!, stored!, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed
            };
        }

        if (string.IsNullOrEmpty(stored))
        {
            SimulateCheck(password);
            return PasswordCheck.Failed;
        }

        // Old row that still holds the plain password: compare in constant time, then re-hash.
        var matches = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(password));
        return matches ? PasswordCheck.SuccessRehashNeeded : PasswordCheck.Failed;
    }

    /// <summary>Spends the same time as a real password check (for an unknown e-mail).</summary>
    public static void SimulateCheck(string password)
    {
        Hasher.VerifyHashedPassword(null!, DummyHash.Value, password);
    }
}
