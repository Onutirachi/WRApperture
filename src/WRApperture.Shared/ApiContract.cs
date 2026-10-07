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

public sealed record HostInfoDto(string HostName, int RdpPort, string AgentVersion, string CertificateFingerprint);

public sealed record ApiErrorDto(string Error, string Message);

/// <summary>
/// A host session in the way of a RemoteApp connection. Action: "logoff" (same user's desktop;
/// open programs are closed) or "disconnect" (another user's active session; programs stay open).
/// </summary>
public sealed record BusySessionDto(string User, string State, bool IsConsole, string Action);

public sealed record SessionStatusDto(bool Busy, IReadOnlyList<BusySessionDto> Sessions);

public sealed record SessionReleaseRequestDto(string? User);

public sealed record SessionReleaseResultDto(int Released, bool Busy);

public static class SessionActions
{
    public const string Logoff = "logoff";
    public const string Disconnect = "disconnect";
}
