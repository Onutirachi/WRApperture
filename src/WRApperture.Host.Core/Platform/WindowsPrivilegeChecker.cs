using System.Security.Principal;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

internal sealed class WindowsPrivilegeChecker : IPrivilegeChecker
{
    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
