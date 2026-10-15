using WRApperture.Host.Core.Sessions;

namespace WRApperture.Host.Core.Tests.Sessions;

public class DesktopShellTests
{
    [Theory]
    [InlineData(@"C:\WINDOWS\Explorer.EXE", true)] // started by Windows at sign-in
    [InlineData(@"""C:\Windows\explorer.exe""", true)]
    [InlineData(null, true)] // unknown: counts as the shell, as before
    [InlineData(@"C:\WINDOWS\explorer.exe /factory,{75dff2b7-6936-4c06-a8bb-676a7b00b24b} -Embedding", false)]
    [InlineData(@"""C:\Windows\explorer.exe"" ""C:\Program Files\Fortinet\FortiClient\FortiTray.exe""", false)]
    [InlineData(@"C:\Windows\explorer.exe C:\Users", false)]
    public void Only_a_bare_explorer_is_the_desktop_shell(string? commandLine, bool shell)
    {
        Assert.Equal(shell, DesktopShell.IsShell(commandLine));
    }
}
