using System.Security.Cryptography;
using System.Text;

namespace Bastion.Core.Security;

/// <summary>
/// Small helper around DPAPI at LocalMachine scope, used to seal data that the
/// SYSTEM service and administrators must be able to read but that should not
/// be portable off the machine (e.g. the recovery reset token). LocalMachine
/// scope is required because the data is produced/consumed across the SYSTEM
/// service and interactive user contexts on the same device.
/// </summary>
public static class MachineSecret
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Bastion.v1.machine-entropy");

    public static string Protect(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var sealed_ = ProtectedData.Protect(data, Entropy, DataProtectionScope.LocalMachine);
        CryptographicOperations.ZeroMemory(data);
        return Convert.ToBase64String(sealed_);
    }

    public static string? Unprotect(string protectedBase64)
    {
        try
        {
            var sealed_ = Convert.FromBase64String(protectedBase64);
            var data = ProtectedData.Unprotect(sealed_, Entropy, DataProtectionScope.LocalMachine);
            var text = Encoding.UTF8.GetString(data);
            CryptographicOperations.ZeroMemory(data);
            return text;
        }
        catch
        {
            return null;
        }
    }
}
