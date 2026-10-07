namespace WRApperture.Shared;

/// <summary>Host Agent HTTP API v1 (see specs/*-host-agent/contracts/http-api.md).</summary>
public static class ApiContract
{
    public const int DefaultPort = 5080;
    public const string TokenHeader = "X-WRApperture-Token";

    public const string Health = "/health";
    public const string Host = "/api/host";
    public const string Apps = "/api/apps";
    public const string Session = "/api/session";
    public const string SessionRelease = "/api/session/release";
    public const int SessionReleaseTimeoutSeconds = 45;

    public const int DefaultIconSize = 48;
    public const int MinIconSize = 16;
    public const int MaxIconSize = 256;

    public static string Icon(string alias) => $"{Apps}/{Uri.EscapeDataString(alias)}/icon";

    public static class Errors
    {
        public const string Unauthorized = "unauthorized";
        public const string TooManyAttempts = "too_many_attempts";
        public const string NotFound = "not_found";
        public const string BadRequest = "bad_request";
    }
}

public sealed record AppSummaryDto(string Alias, string DisplayName, string IconUrl);

/// <param name="Features">Optional capabilities (<see cref="AgentFeatures"/>); null from agents older than 0.6.0.</param>
public sealed record HostInfoDto(string HostName, int RdpPort, string AgentVersion, string CertificateFingerprint,
    IReadOnlyList<string>? Features = null);

public static class AgentFeatures
{
    /// <summary>The session API understands <see cref="SessionModes.Desktop"/>.</summary>
    public const string DesktopSession = "desktop-session";
}

/// <summary>What the client is about to open; decides which host sessions are in the way.</summary>
public static class SessionModes
{
    public const string RemoteApp = "remoteapp";
    public const string Desktop = "desktop";

    /// <summary>Null/empty means RemoteApp (clients older than 0.6.0); unknown values are rejected.</summary>
    public static bool TryParse(string? value, out bool desktop)
    {
        desktop = string.Equals(value, Desktop, StringComparison.OrdinalIgnoreCase);
        return desktop || string.IsNullOrEmpty(value) || string.Equals(value, RemoteApp, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ApiErrorDto(string Error, string Message);

/// <summary>
/// A host session in the way of the connection. Action: "logoff" (same user's session that cannot be
/// reused in the requested mode; open programs are closed) or "disconnect" (another user's active
/// session; programs stay open). Kind: "desktop" or "remoteapp" (null from agents older than 0.6.0).
/// </summary>
public sealed record BusySessionDto(string User, string State, bool IsConsole, string Action, string? Kind = null);

public sealed record SessionStatusDto(bool Busy, IReadOnlyList<BusySessionDto> Sessions);

public sealed record SessionReleaseRequestDto(string? User, string? Mode = null);

public sealed record SessionReleaseResultDto(int Released, bool Busy);

/// <summary>BusySessionDto.State codes (agents older than 0.8.0 sent Portuguese text instead).</summary>
public static class SessionStates
{
    public const string Active = "active";
    public const string Disconnected = "disconnected";
}

public static class SessionActions
{
    public const string Logoff = "logoff";
    public const string Disconnect = "disconnect";
}
