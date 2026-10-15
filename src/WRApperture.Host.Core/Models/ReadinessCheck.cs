namespace WRApperture.Host.Core.Models;

public enum ReadinessCheckId
{
    Edition,
    RdpEnabled,
    Nla,
    AllowListEnforced,
    FirewallRdp,
    LanguageBarHidden,

    /// <summary>A policy value (HKLM\SOFTWARE\Policies\…\Terminal Services) that overrides <see cref="RdpEnabled"/>.</summary>
    RdpPolicy,

    /// <summary>The Remote Desktop service (TermService) is not disabled and, with Remote Desktop on, is running.</summary>
    RdpService,

    /// <summary>No connected network is Public (Windows blocks incoming connections there).</summary>
    NetworkProfile,

    /// <summary>The console user's local account has a password (Windows refuses remote sign-in without one).</summary>
    AccountPassword,

    /// <summary>The console user is an administrator or in Remote Desktop Users.</summary>
    RemoteDesktopUsers,

    /// <summary>The console user signs in with a Microsoft account: RDP takes its e-mail and password, not the PIN.</summary>
    MicrosoftAccount,

    /// <summary>The PC goes to sleep when idle and is then unreachable.</summary>
    Sleep,
}

public enum ReadinessState
{
    Ok,
    Pending,
    NotSupported,

    /// <summary>Nothing blocks remote access, but the user should know (e.g. which user name to type).</summary>
    Advice,
}

/// <summary>How a check that is not OK can be solved.</summary>
public enum ReadinessFix
{
    None,

    /// <summary><see cref="IHostPreparation.Fix"/> solves it (requires admin).</summary>
    Automatic,

    /// <summary><see cref="IHostPreparation.SetAccountPassword"/>, with a password the user types.</summary>
    SetPassword,

    /// <summary>Only the user can change it, in the Windows settings page <see cref="ReadinessCheck.SettingsUri"/>.</summary>
    OpenSettings,
}

/// <param name="Fix">How to solve it when not OK.</param>
/// <param name="SettingsUri">The related Windows settings page ("ms-settings:…"), if any.</param>
public sealed record ReadinessCheck(
    ReadinessCheckId Id,
    string Description,
    ReadinessState State,
    string CurrentValue,
    string ExpectedValue,
    ReadinessFix Fix = ReadinessFix.None,
    string? SettingsUri = null);
