using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WRApperture.Shared;

/// <summary>SHA-256 of the certificate DER, uppercase hex. The value clients pin.</summary>
public static class CertificateFingerprint
{
    public static string Compute(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

    /// <summary>Groups of 4 hex chars, for humans comparing values on two screens.</summary>
    public static string Format(string fingerprint) =>
        string.Join(' ', Enumerable.Range(0, (fingerprint.Length + 3) / 4)
            .Select(i => fingerprint.Substring(i * 4, Math.Min(4, fingerprint.Length - (i * 4)))));
}
