namespace WRApperture.Host.Core.Abstractions;

/// <summary>A Windows account as Remote Desktop sees it.</summary>
/// <param name="UserName">SAM account name ("carlos"), also for Microsoft accounts.</param>
/// <param name="Domain">The computer name for local accounts.</param>
/// <param name="IsLocal">Lives on this PC (local or Microsoft account), not on a domain.</param>
/// <param name="MicrosoftAccount">The Microsoft account e-mail linked to it, if any.</param>
/// <param name="IsAdministrator">Member of Administrators (directly or through a group).</param>
/// <param name="IsRemoteDesktopUser">Member of Remote Desktop Users (directly or through a group).</param>
public sealed record LocalAccount(
    string UserName,
    string Domain,
    string Sid,
    bool IsLocal,
    string? MicrosoftAccount,
    bool IsAdministrator,
    bool IsRemoteDesktopUser)
{
    /// <summary>Administrators and Remote Desktop Users may sign in remotely by default.</summary>
    public bool CanSignInRemotely => IsAdministrator || IsRemoteDesktopUser;

    /// <summary>What to type as user name in the Remote Desktop sign-in: the e-mail, or "PC\user".</summary>
    public string RemoteUserName => MicrosoftAccount ?? $@"{Domain}\{UserName}";
}

public enum PasswordStatus
{
    Set,
    Blank,

    /// <summary>Could not be determined (e.g. domain account, locked or disabled).</summary>
    Unknown,
}

/// <summary>Accounts of this PC, as far as Remote Desktop sign-in is concerned.</summary>
public interface ILocalAccounts
{
    /// <summary>The user signed in at the PC's screen, or null when nobody is.</summary>
    LocalAccount? ConsoleUser();

    PasswordStatus GetPasswordStatus(LocalAccount account);

    /// <summary>Sets a new password without knowing the old one. Requires admin; local accounts only.</summary>
    void SetPassword(LocalAccount account, string password);

    /// <summary>Requires admin. Idempotent.</summary>
    void AddToRemoteDesktopUsers(string sid);

    /// <summary>Requires admin. Idempotent.</summary>
    void RemoveFromRemoteDesktopUsers(string sid);
}
