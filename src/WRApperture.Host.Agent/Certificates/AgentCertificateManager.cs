using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WRApperture.Host.Core.Agent;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Certificates;

/// <summary>Holds the identity the agent presents to clients.</summary>
internal sealed class AgentIdentity
{
    public string Fingerprint { get; set; } = string.Empty;
}

/// <summary>
/// Loads the agent's self-signed TLS certificate from LocalMachine\My, creating or renewing it when
/// missing, lacking its private key, or expiring within <see cref="RenewBefore"/>.
/// </summary>
internal sealed class AgentCertificateManager(
    IAgentSettingsStore settings,
    AgentIdentity identity,
    TimeProvider time,
    ILogger<AgentCertificateManager> logger)
{
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);
    private static readonly TimeSpan Validity = TimeSpan.FromDays(5 * 365);
    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";

    public X509Certificate2 EnsureCertificate()
    {
        var thumbprint = settings.Load().CertificateThumbprint;
        var certificate = FindUsable(thumbprint) ?? CreateAndStore(thumbprint);
        identity.Fingerprint = CertificateFingerprint.Compute(certificate);
        logger.LogInformation("Agent certificate {Thumbprint} valid until {NotAfter:yyyy-MM-dd}, fingerprint {Fingerprint}",
            certificate.Thumbprint, certificate.NotAfter, CertificateFingerprint.Format(identity.Fingerprint));
        return certificate;
    }

    private X509Certificate2? FindUsable(string? thumbprint)
    {
        if (thumbprint is null)
        {
            return null;
        }

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        var certificate = found.Count > 0 ? found[0] : null;
        foreach (var other in found.Where(c => !ReferenceEquals(c, certificate)))
        {
            other.Dispose();
        }

        if (certificate is { HasPrivateKey: true } && certificate.NotAfter.ToUniversalTime() - time.GetUtcNow().UtcDateTime > RenewBefore)
        {
            return certificate;
        }

        certificate?.Dispose();
        logger.LogInformation("Agent certificate {Thumbprint} missing, without private key or expiring; renewing", thumbprint);
        return null;
    }

    private X509Certificate2 CreateAndStore(string? previousThumbprint)
    {
        var machine = Environment.MachineName;
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=WRApperture Agent ({machine})", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(machine);
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthOid)], false));

        var now = time.GetUtcNow();
        using var ephemeral = request.CreateSelfSigned(now.AddDays(-1), now + Validity);
        var persisted = new X509Certificate2(
            ephemeral.Export(X509ContentType.Pfx), (string?)null,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

        using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
        {
            store.Open(OpenFlags.ReadWrite);
            store.Add(persisted);
            if (previousThumbprint is not null)
            {
                foreach (var old in store.Certificates.Find(X509FindType.FindByThumbprint, previousThumbprint, validOnly: false))
                {
                    store.Remove(old);
                    old.Dispose();
                }
            }
        }

        settings.SetCertificateThumbprint(persisted.Thumbprint);
        logger.LogInformation("Created agent certificate {Thumbprint}", persisted.Thumbprint);
        return persisted;
    }
}
