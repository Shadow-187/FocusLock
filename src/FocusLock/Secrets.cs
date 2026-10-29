using System.Security.Cryptography;
using System.Text;

namespace FocusLock;

/// <summary>
/// Encrypts secrets (the API key) with Windows DPAPI before they touch the disk.
/// CurrentUser scope means only the same Windows account on the same computer can decrypt it,
/// so a copied settings.json is useless on its own.
/// </summary>
public static class Secrets
{
    // Extra entropy so other programs running as the same user can't just call
    // ProtectedData.Unprotect on the blob without knowing it came from FocusLock.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FocusLock.ApiKey.v1");

    public static string Protect(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain.Trim());
        try
        {
            var enc = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Returns "" if the blob is empty, damaged, or from another user/computer.</summary>
    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64)) return "";
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy,
                                            DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return "";
        }
        finally
        {
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
        }
    }
}
