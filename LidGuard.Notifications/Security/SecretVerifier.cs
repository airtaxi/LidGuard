using System.Security.Cryptography;
using System.Text;

namespace LidGuard.Notifications.Security;

internal static class SecretVerifier
{
    public static bool EqualsConfiguredSecret(string configuredSecret, string suppliedSecret)
    {
        if (string.IsNullOrEmpty(configuredSecret) || string.IsNullOrEmpty(suppliedSecret)) return false;

        var configuredSecretBytes = Encoding.UTF8.GetBytes(configuredSecret);
        var suppliedSecretBytes = Encoding.UTF8.GetBytes(suppliedSecret);
        return configuredSecretBytes.Length == suppliedSecretBytes.Length
            && CryptographicOperations.FixedTimeEquals(configuredSecretBytes, suppliedSecretBytes);
    }
}
