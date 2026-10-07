using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WRApperture.Client.Core.Credentials;

public interface ICredentialStore
{
    void Save(string address, string userName, string password);
    bool Exists(string address);
    void Delete(string address);
}

/// <summary>
/// Stores RDP credentials in the Windows Credential Manager under "TERMSRV/&lt;address&gt;" as a
/// domain-password credential: exactly what mstsc reads, so no login prompt appears.
/// </summary>
public sealed class WindowsCredentialStore(string targetPrefix = WindowsCredentialStore.TermsrvPrefix) : ICredentialStore
{
    public const string TermsrvPrefix = "TERMSRV/";
    private const int CredTypeDomainPassword = 2;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string Target(string address) => targetPrefix + address.Trim();

    public void Save(string address, string userName, string password)
    {
        var blob = Encoding.Unicode.GetBytes(password);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new Native.Credential
            {
                Type = CredTypeDomainPassword,
                TargetName = Target(address),
                UserName = userName.Trim(),
                CredentialBlob = blobPtr,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CredPersistLocalMachine,
            };
            if (!Native.CredWriteW(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível salvar a credencial no Windows.");
            }
        }
        finally
        {
            // Zero the plain-text password copy before freeing it.
            Marshal.Copy(new byte[blob.Length], 0, blobPtr, blob.Length);
            Marshal.FreeHGlobal(blobPtr);
            Array.Clear(blob);
        }
    }

    public bool Exists(string address)
    {
        if (!Native.CredReadW(Target(address), CredTypeDomainPassword, 0, out var ptr))
        {
            return false;
        }

        Native.CredFree(ptr);
        return true;
    }

    public void Delete(string address)
    {
        if (Native.CredDeleteW(Target(address), CredTypeDomainPassword, 0))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error, $"Não foi possível remover a credencial do Windows (erro {error}).");
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Credential
        {
            public uint Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWriteW(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredReadW(string target, int type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredDeleteW(string target, int type, uint flags);

        [DllImport("advapi32.dll")]
        public static extern void CredFree(IntPtr buffer);
    }
}
