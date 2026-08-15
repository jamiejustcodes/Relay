using System.Security.Cryptography;
using System.Text;
using ScreenLens.Core.Interfaces;

namespace ScreenLens.Infrastructure.Security;

/// <summary>
/// Secures sensitive data (like Gemini API keys) using Windows Data Protection API (DPAPI).
/// Data is encrypted per Windows User account.
/// </summary>
public class DpapiSecretVault : ISecretVault
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("Relay.Entropy.v1");
    private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("ScreenLens.Entropy.v1");

    public string EncryptSecret(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = ProtectedData.Protect(plainBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipherBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public string DecryptSecret(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        try
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherText);
            try
            {
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // Fallback to legacy entropy
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, LegacyEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
        }
        catch
        {
            return string.Empty;
        }
    }
}
