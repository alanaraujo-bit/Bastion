using System.Security.Cryptography;
using System.Text;

namespace Bastion.Core.Security;

/// <summary>
/// DPAPI at CurrentUser scope. Used for the Windows Hello convenience path:
/// after the user proves the master password once, it is sealed here so a
/// successful Hello verification (which only the interactive user can pass) can
/// unseal it and present the real credential to the service. Only the same
/// Windows user on the same machine can unseal it.
/// </summary>
public static class UserSecret
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Bastion.v1.hello");

    public static string StorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        BastionPaths.ProductName, "hello.bin");

    public static bool Exists => File.Exists(StorePath);

    public static void Seal(string secret)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var data = Encoding.UTF8.GetBytes(secret);
        var sealed_ = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(data);
        File.WriteAllBytes(StorePath, sealed_);
    }

    public static string? Unseal()
    {
        try
        {
            if (!File.Exists(StorePath)) return null;
            var sealed_ = File.ReadAllBytes(StorePath);
            var data = ProtectedData.Unprotect(sealed_, Entropy, DataProtectionScope.CurrentUser);
            var text = Encoding.UTF8.GetString(data);
            CryptographicOperations.ZeroMemory(data);
            return text;
        }
        catch { return null; }
    }

    public static void Clear()
    {
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { }
    }
}
