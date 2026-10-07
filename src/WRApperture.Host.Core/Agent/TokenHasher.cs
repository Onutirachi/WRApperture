using System.Security.Cryptography;
using System.Text;

namespace WRApperture.Host.Core.Agent;

/// <summary>
/// Tokens carry 256 bits of entropy, so a salted SHA-256 is sufficient (no slow KDF needed).
/// </summary>
internal static class TokenHasher
{
    public const int TokenBytes = 32;
    public const int SaltBytes = 16;

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltBytes);

    public static byte[] Hash(string token, byte[] salt)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var input = new byte[salt.Length + tokenBytes.Length];
        salt.CopyTo(input, 0);
        tokenBytes.CopyTo(input, salt.Length);
        return SHA256.HashData(input);
    }

    public static bool Verify(string token, byte[] salt, byte[] expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(token, salt), expectedHash);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
