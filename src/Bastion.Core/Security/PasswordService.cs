using System.Security.Cryptography;
using System.Text;
using Bastion.Core.Models;
using Konscious.Security.Cryptography;

namespace Bastion.Core.Security;

/// <summary>
/// Creates and verifies master-password / recovery-answer verifiers using
/// Argon2id. The plaintext is never persisted; only salt + hash + parameters.
/// Verification is constant-time.
/// </summary>
public static class PasswordService
{
    // Defaults chosen to be resistant on desktop hardware while keeping the
    // unlock prompt responsive (~tens of ms). Stored per-credential so they can
    // be tuned upward in future versions without breaking existing hashes.
    private const int DefaultMemoryKiB = 65536; // 64 MiB
    private const int DefaultIterations = 3;
    private const int DefaultParallelism = 2;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static MasterCredential CreateMaster(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Hash(password, salt, DefaultMemoryKiB, DefaultIterations, DefaultParallelism);
        return new MasterCredential
        {
            Algorithm = "argon2id",
            MemoryKiB = DefaultMemoryKiB,
            Iterations = DefaultIterations,
            Parallelism = DefaultParallelism,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash),
            SetUtc = DateTimeOffset.UtcNow,
        };
    }

    public static bool VerifyMaster(MasterCredential cred, string password)
    {
        if (cred is null || !cred.IsConfigured || string.IsNullOrEmpty(password))
            return false;
        try
        {
            var salt = Convert.FromBase64String(cred.Salt);
            var expected = Convert.FromBase64String(cred.Hash);
            var actual = Hash(password, salt, cred.MemoryKiB, cred.Iterations, cred.Parallelism, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Builds a recovery verifier from a normalized answer.</summary>
    public static (string salt, string hash) CreateRecoveryAnswer(string answer)
    {
        var normalized = NormalizeAnswer(answer);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Hash(normalized, salt, DefaultMemoryKiB, DefaultIterations, DefaultParallelism);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool VerifyRecoveryAnswer(RecoverySettings recovery, string answer)
    {
        if (recovery is null || !recovery.Enabled ||
            string.IsNullOrEmpty(recovery.AnswerSalt) || string.IsNullOrEmpty(recovery.AnswerHash))
            return false;
        try
        {
            var normalized = NormalizeAnswer(answer);
            var salt = Convert.FromBase64String(recovery.AnswerSalt);
            var expected = Convert.FromBase64String(recovery.AnswerHash);
            var actual = Hash(normalized, salt, DefaultMemoryKiB, DefaultIterations, DefaultParallelism, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Recovery answers are matched case-insensitively with collapsed whitespace.</summary>
    private static string NormalizeAnswer(string answer) =>
        string.Join(' ', (answer ?? "").Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static byte[] Hash(string password, byte[] salt, int memoryKiB, int iterations, int parallelism, int outLen = HashBytes)
    {
        var pwdBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(pwdBytes)
            {
                Salt = salt,
                MemorySize = memoryKiB,
                Iterations = iterations,
                DegreeOfParallelism = parallelism,
            };
            return argon.GetBytes(outLen);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwdBytes);
        }
    }

    /// <summary>A rough 0..4 strength score for onboarding feedback (not stored).</summary>
    public static int EstimateStrength(string password)
    {
        if (string.IsNullOrEmpty(password)) return 0;
        int score = 0;
        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        int classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        if (classes >= 2) score++;
        if (classes >= 3) score++;
        return Math.Min(score, 4);
    }
}
