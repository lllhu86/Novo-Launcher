using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinecraftLauncher.Services;

public static class DataProtectionHelper
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NovoLauncher_DPAPI_Entropy_v1");

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    public static string Unprotect(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return encryptedText;

        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedText);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return encryptedText;
        }
    }

    public static bool IsProtected(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(text);
            ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
