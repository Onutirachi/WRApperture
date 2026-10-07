using System.Security.AccessControl;
using System.Security.Principal;

namespace WRApperture.Host.Core.Platform;

/// <summary>
/// Where the host downloads setups it will run elevated: %ProgramData%\WRApperture\updates, writable only by
/// SYSTEM and Administrators. ProgramData lets standard users create folders, so a folder they could have
/// planted (not owned by SYSTEM or Administrators) is deleted and created again.
/// </summary>
public static class UpdateFolder
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WRApperture", "updates");

    /// <summary>A fresh, empty <c>updates\&lt;name&gt;</c> ("agent", "manager"). Requires SYSTEM or admin.</summary>
    public static string Prepare(string name)
    {
        var root = new DirectoryInfo(Root);
        if (root.Exists && !IsTrusted(root))
        {
            root.Delete(recursive: true);
            root.Refresh();
        }

        if (root.Exists)
        {
            root.SetAccessControl(Security());
        }
        else
        {
            root.Parent!.Create();
            root.Create(Security());
        }

        var folder = new DirectoryInfo(Path.Combine(Root, name));
        if (folder.Exists)
        {
            folder.Delete(recursive: true);
        }

        folder.Create(); // inherits the protected ACL
        return folder.FullName;
    }

    private static bool IsTrusted(DirectoryInfo folder) =>
        folder.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner &&
        (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));

    /// <summary>SYSTEM and Administrators only, nothing inherited from ProgramData.</summary>
    private static DirectorySecurity Security()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        return security;
    }
}
