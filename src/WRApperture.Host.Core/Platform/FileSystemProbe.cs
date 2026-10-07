using System.Security.AccessControl;
using System.Security.Principal;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

internal sealed class FileSystemProbe : IFileSystemProbe
{
    // Users, Authenticated Users, Everyone, INTERACTIVE
    private static readonly HashSet<string> NonAdminSids = ["S-1-5-32-545", "S-1-5-11", "S-1-1-0", "S-1-5-4"];

    private const FileSystemRights WriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public bool FileExists(string path) => File.Exists(path);

    public string Expand(string path) => Environment.ExpandEnvironmentVariables(path);

    public bool IsUserWritableLocation(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        var profilesRoot = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (profilesRoot is not null &&
            dir.StartsWith(profilesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var acl = new DirectoryInfo(dir).GetAccessControl();
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow &&
                    NonAdminSids.Contains(rule.IdentityReference.Value) &&
                    (rule.FileSystemRights & WriteRights) != 0)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }

        return false;
    }
}
