using Windows.Security.Credentials.UI;
using Bastion.Core.Security;

namespace Bastion.Ui.Services;

/// <summary>
/// Windows Hello integration. Availability check plus a verification prompt
/// (PIN / fingerprint / face). The convenience path binds Hello to the real
/// master credential: after the user proves the password once, it is sealed
/// per-user with DPAPI; a successful Hello verification unseals it and the real
/// credential is what the service ultimately checks.
/// </summary>
public static class HelloService
{
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            var a = await UserConsentVerifier.CheckAvailabilityAsync();
            return a == UserConsentVerifierAvailability.Available;
        }
        catch { return false; }
    }

    public static async Task<bool> VerifyAsync(string message)
    {
        try
        {
            var r = await UserConsentVerifier.RequestVerificationAsync(message);
            return r == UserConsentVerificationResult.Verified;
        }
        catch { return false; }
    }

    public static bool IsEnrolled => UserSecret.Exists;

    /// <summary>Seal the verified master password so Hello can present it later.</summary>
    public static void Enroll(string masterPassword) => UserSecret.Seal(masterPassword);

    public static void Unenroll() => UserSecret.Clear();

    /// <summary>Run Hello, and on success return the sealed master password (or null).</summary>
    public static async Task<string?> VerifyAndUnsealAsync(string message)
    {
        if (!IsEnrolled) return null;
        if (!await VerifyAsync(message)) return null;
        return UserSecret.Unseal();
    }
}
