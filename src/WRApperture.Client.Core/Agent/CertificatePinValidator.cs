using System.Security.Cryptography.X509Certificates;
using WRApperture.Shared;

namespace WRApperture.Client.Core.Agent;

/// <summary>
/// Trust-on-first-use pinning. The agent's certificate is self-signed, so chain and name errors are
/// ignored on purpose: trust comes solely from the SHA-256 fingerprint the user confirmed.
/// </summary>
public sealed class CertificatePinValidator
{
    private readonly string? _pinned;

    private CertificatePinValidator(string? pinned) => _pinned = pinned;

    /// <summary>Accepts any certificate and records its fingerprint (pairing).</summary>
    public static CertificatePinValidator ForPairing() => new(null);

    public static CertificatePinValidator Pinned(string fingerprint) => new(fingerprint);

    public string? Observed { get; private set; }

    /// <summary>Set when the presented certificate did not match the pin.</summary>
    public string? Mismatch { get; private set; }

    public bool Validate(X509Certificate? certificate)
    {
        if (certificate is null)
        {
            return false;
        }

        var fingerprint = CertificateFingerprint.Compute(certificate);
        Observed = fingerprint;
        if (_pinned is null || string.Equals(fingerprint, _pinned, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Mismatch = fingerprint;
        return false;
    }
}
