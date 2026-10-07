using System.Security.Principal;
using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Readiness;

/// <summary>
/// RemoteApp sessions have no taskbar, so Windows shows the input language bar as a floating
/// window over the apps. ShowStatus = 3 hides it (takes effect in new sessions).
/// </summary>
public interface ILanguageBarSettings
{
    string CurrentUserSid { get; }
    bool IsHidden(string sid);
    void Hide(string sid);

    /// <summary>Removes the override, restoring the Windows default.</summary>
    void Show(string sid);
}

internal sealed class LanguageBarSettings(IUserRegistryAccess users) : ILanguageBarSettings
{
    public const string Path = @"Software\Microsoft\CTF\LangBar";
    public const string Name = "ShowStatus";
    public const int Hidden = 3;

    public string CurrentUserSid => WindowsIdentity.GetCurrent().User!.Value;

    public bool IsHidden(string sid) => users.GetValue(sid, Path, Name) is Hidden;

    public void Hide(string sid) => users.SetValue(sid, Path, Name, Hidden, RegistryValueKind.DWord);

    public void Show(string sid) => users.DeleteValue(sid, Path, Name);
}
