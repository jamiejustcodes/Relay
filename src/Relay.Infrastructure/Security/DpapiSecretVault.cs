using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Relay.Core.Interfaces;
using Relay.Infrastructure.ScreenCapture;

namespace Relay.Infrastructure.Security;

/// <summary>
/// Secures sensitive data (like Gemini API keys) using Windows Data Protection API (DPAPI).
/// Data is encrypted per Windows User account and protected against debugger attachment.
/// </summary>
public class DpapiSecretVault : ISecretVault
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("Relay.Entropy.v1");
    private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("ScreenLens.Entropy.v1");

    /// <summary>
    /// Checks whether an unauthorized debugger (e.g. ICorDebug, x64dbg) is attached.
    /// </summary>
    public static bool IsDebuggerAttached()
    {
        try
        {
            if (Debugger.IsAttached) return true;
            if (NativeMethods.IsDebuggerPresent()) return true;

            bool isRemoteDebugger = false;
            using var proc = Process.GetCurrentProcess();
            if (NativeMethods.CheckRemoteDebuggerPresent(proc.Handle, ref isRemoteDebugger) && isRemoteDebugger)
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    public string EncryptSecret(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        try
        {
            byte[] cipherBytes = ProtectedData.Protect(plainBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipherBytes);
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            // Zero-out sensitive plaintext memory buffer
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    public string DecryptSecret(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        // Anti-Debugger Security Check
        if (IsDebuggerAttached())
        {
            Debug.WriteLine("[SecurityGuard] Unauthorized debugger attachment detected. Aborting secret decryption.");
            return string.Empty;
        }

        try
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherText);
            byte[]? plainBytes = null;
            try
            {
                try
                {
                    plainBytes = ProtectedData.Unprotect(cipherBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plainBytes);
                }
                catch
                {
                    // Fallback to legacy entropy
                    plainBytes = ProtectedData.Unprotect(cipherBytes, LegacyEntropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
            finally
            {
                if (plainBytes != null)
                {
                    // Zero-out sensitive plaintext memory buffer
                    Array.Clear(plainBytes, 0, plainBytes.Length);
                }
            }
        }
        catch
        {
            return string.Empty;
        }
    }
}
