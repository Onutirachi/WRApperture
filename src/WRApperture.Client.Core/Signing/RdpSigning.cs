using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using WRApperture.Shared;

namespace WRApperture.Client.Core.Signing;

public sealed record SigningStatus(bool Configured, string? Sha256, string? Thumbprint)
{
    public static SigningStatus NotConfigured { get; } = new(false, null, null);
}

public interface IRdpSigner
{
    SigningStatus GetStatus();

    /// <summary>Signs in place. Returns false (file left unsigned) when not configured or on failure.</summary>
    bool TrySign(string rdpFile);
}

/// <summary>
/// Since the April 2026 Windows update, unsigned .rdp files show a security warning on every
/// launch. rdpsign.exe only uses certificates from LocalMachine\My, so a one-time elevated setup
/// creates a non-exportable machine key (usable by configured users only) with a self-signed
/// certificate, and lists its thumbprint in the "trusted .rdp publishers" policy. The certificate
/// is deliberately NOT added to Root/TrustedPublisher: trust is limited to RDP files.
/// </summary>
public sealed class RdpSigning : IRdpSigner
{
    public const string SubjectPrefix = "CN=WRApperture RDP Signing";
    public const string KeyName = "WRApperture-RdpSigning";
    public const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
    public const string PolicyValue = "TrustedCertThumbprints";
    /// <summary>rdpsign rejects (0x8007000D) certificates without the Code Signing EKU (verified).</summary>
    private const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";

    /// <summary>
    /// The Root-store copy is restricted to "Remote Desktop Authentication" (CERT_ENHKEY_USAGE_PROP_ID),
    /// so Windows does not trust it for Authenticode even though the certificate carries Code Signing.
    /// </summary>
    private const string RemoteDesktopAuthenticationOid = "1.3.6.1.4.1.311.54.1.2";
    private const int CertEnhancedKeyUsagePropId = 9;
    private const CngPropertyOptions DaclSecurityInformation = (CngPropertyOptions)4;
    private static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    private readonly IProcessRunner _runner;
    private readonly Func<SigningStatus> _status;
    private readonly ILogger _logger;

    public RdpSigning(IProcessRunner runner, ILogger<RdpSigning>? logger = null)
        : this(runner, null, logger)
    {
    }

    internal RdpSigning(IProcessRunner runner, Func<SigningStatus>? status, ILogger? logger)
    {
        _runner = runner;
        _logger = logger ?? NullLogger<RdpSigning>.Instance;
        _status = status ?? ReadStatus;
    }

    public static string RdpSignPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rdpsign.exe");

    public SigningStatus GetStatus() => _status();

    public bool TrySign(string rdpFile)
    {
        var status = GetStatus();
        if (!status.Configured)
        {
            return false;
        }

        // Verified on Windows 11: despite its name, rdpsign's /sha256 switch locates the certificate
        // by its standard (SHA-1) thumbprint; the SHA-256 hash yields CRYPT_E_NOT_FOUND.
        var exit = _runner.Run(RdpSignPath, ["/sha256", status.Thumbprint!, "/q", rdpFile], out var output);
        if (exit == 0)
        {
            return true;
        }

        _logger.LogWarning("rdpsign failed with {Exit} for {File}: {Output}", exit, rdpFile, output.Trim());
        return false;
    }

    /// <summary>Elevated. Idempotent; adds <paramref name="userSid"/> to the key's users.</summary>
    public void Setup(string userSid)
    {
        _ = new SecurityIdentifier(userSid); // validates the argument

        var certificate = FindUsableCertificate();
        if (certificate is null)
        {
            var stale = RemoveCertificates();
            certificate = CreateCertificate(userSid);
            SetPolicy(ThumbprintList.Remove(ReadPolicy(), stale));
            _logger.LogInformation("Created RDP signing certificate {Thumbprint}", certificate.Thumbprint);
        }
        else
        {
            GrantUser(userSid);
        }

        using (certificate)
        {
            // Verified: mstsc only honors the thumbprint policy when the chain is trusted (Root).
            TrustInRoot(certificate);
            SetPolicy(ThumbprintList.Add(ReadPolicy(), certificate.Thumbprint));
        }

        _logger.LogInformation("RDP signing configured for {Sid}", userSid);
    }

    /// <summary>Elevated. Removes certificate (My and Root), key and policy entries.</summary>
    public void Remove()
    {
        var thumbprints = RemoveCertificates();
        RemoveFromRoot(thumbprints);
        DeleteKey();
        SetPolicy(ThumbprintList.Remove(ReadPolicy(), thumbprints));
        _logger.LogInformation("RDP signing removed ({Count} certificates)", thumbprints.Count);
    }

    /// <summary>Adds the public certificate to LocalMachine\Root, enabled only for Remote Desktop Authentication.</summary>
    private static void TrustInRoot(X509Certificate2 certificate)
    {
        using var root = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        root.Open(OpenFlags.ReadWrite);
        using (var publicOnly = new X509Certificate2(certificate.RawData))
        {
            root.Add(publicOnly);
        }

        var restriction = new X509EnhancedKeyUsageExtension([new Oid(RemoteDesktopAuthenticationOid)], false).RawData;
        foreach (var stored in root.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false))
        {
            using (stored)
            {
                Native.SetProperty(stored, CertEnhancedKeyUsagePropId, restriction);
            }
        }
    }

    private static void RemoveFromRoot(IReadOnlyCollection<string> thumbprints)
    {
        using var root = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        root.Open(OpenFlags.ReadWrite);
        foreach (var certificate in root.Certificates)
        {
            if (thumbprints.Contains(certificate.Thumbprint, StringComparer.OrdinalIgnoreCase) ||
                certificate.Subject.StartsWith(SubjectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                root.Remove(certificate);
            }

            certificate.Dispose();
        }
    }

    private static bool IsTrustedInRoot(string thumbprint)
    {
        using var root = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        root.Open(OpenFlags.ReadOnly);
        var found = root.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        foreach (var certificate in found)
        {
            certificate.Dispose();
        }

        return found.Count > 0;
    }

    private static class Native
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct CryptDataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CertSetCertificateContextProperty(IntPtr context, int propId, int flags, ref CryptDataBlob data);

        /// <summary>Sets a property on a store-backed certificate context; persisted because the store is open read-write.</summary>
        public static void SetProperty(X509Certificate2 certificate, int propId, byte[] value)
        {
            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(value.Length);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(value, 0, buffer, value.Length);
                var blob = new CryptDataBlob { Size = value.Length, Data = buffer };
                if (!CertSetCertificateContextProperty(certificate.Handle, propId, 0, ref blob))
                {
                    throw new CryptographicException(System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private SigningStatus ReadStatus()
    {
        try
        {
            using var certificate = FindUsableCertificate();
            if (certificate is null ||
                !ThumbprintList.Contains(ReadPolicy(), certificate.Thumbprint) ||
                !IsTrustedInRoot(certificate.Thumbprint))
            {
                return SigningStatus.NotConfigured;
            }

            return new SigningStatus(true, Convert.ToHexString(SHA256.HashData(certificate.RawData)), certificate.Thumbprint);
        }
        catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Could not read RDP signing status");
            return SigningStatus.NotConfigured;
        }
    }

    private static X509Certificate2? FindUsableCertificate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        X509Certificate2? best = null;
        foreach (var certificate in store.Certificates)
        {
            var usable = certificate.Subject.StartsWith(SubjectPrefix, StringComparison.OrdinalIgnoreCase) &&
                         certificate.HasPrivateKey &&
                         certificate.NotAfter.ToUniversalTime() - DateTime.UtcNow > RenewBefore &&
                         (best is null || certificate.NotAfter > best.NotAfter);
            if (usable)
            {
                best?.Dispose();
                best = certificate;
            }
            else
            {
                certificate.Dispose();
            }
        }

        return best;
    }

    private static X509Certificate2 CreateCertificate(string userSid)
    {
        var parameters = new CngKeyCreationParameters
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            KeyCreationOptions = CngKeyCreationOptions.MachineKey | CngKeyCreationOptions.OverwriteExistingKey,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
        };
        parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
        parameters.Parameters.Add(new CngProperty("Security Descr",
            SigningKeySecurity.ToBinary(SigningKeySecurity.Initial(userSid)), DaclSecurityInformation));

        using var key = CngKey.Create(CngAlgorithm.Rsa, KeyName, parameters);
        using var rsa = new RSACng(key);
        var request = new CertificateRequest($"{SubjectPrefix} ({Environment.MachineName})", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(CodeSigningOid)], false));

        var now = DateTimeOffset.UtcNow;
        var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));
        certificate.FriendlyName = "WRApperture - assinatura de conexões RDP";

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
        return certificate;
    }

    private static void GrantUser(string userSid)
    {
        using var key = CngKey.Open(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey);
        var current = SigningKeySecurity.FromBinary(key.GetProperty("Security Descr", DaclSecurityInformation).GetValue()!);
        var updated = SigningKeySecurity.WithUser(current, userSid);
        if (updated != current)
        {
            key.SetProperty(new CngProperty("Security Descr", SigningKeySecurity.ToBinary(updated), DaclSecurityInformation));
        }
    }

    private static List<string> RemoveCertificates()
    {
        var removed = new List<string>();
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        foreach (var certificate in store.Certificates)
        {
            if (certificate.Subject.StartsWith(SubjectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                store.Remove(certificate);
                removed.Add(certificate.Thumbprint);
            }

            certificate.Dispose();
        }

        return removed;
    }

    private static void DeleteKey()
    {
        if (CngKey.Exists(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey))
        {
            using var key = CngKey.Open(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey);
            key.Delete();
        }
    }

    private static string? ReadPolicy()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(PolicyPath);
        return key?.GetValue(PolicyValue) as string;
    }

    private static void SetPolicy(string? value)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        if (value is null)
        {
            using var existing = root.OpenSubKey(PolicyPath, writable: true);
            existing?.DeleteValue(PolicyValue, throwOnMissingValue: false);
            return;
        }

        using var key = root.CreateSubKey(PolicyPath, writable: true);
        key.SetValue(PolicyValue, value, RegistryValueKind.String);
    }

    /// <summary>Formatted SHA-256 for display.</summary>
    public static string Describe(SigningStatus status) =>
        status.Sha256 is null ? "não configurado" : CertificateFingerprint.Format(status.Sha256);
}
