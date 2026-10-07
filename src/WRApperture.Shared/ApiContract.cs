namespace WRApperture.Shared;

/// <summary>Host Agent HTTP API v1 (see specs/*-host-agent/contracts/http-api.md).</summary>
public static class ApiContract
{
    public const int DefaultPort = 5080;
    public const string TokenHeader = "X-WRApperture-Token";

    public const string Health = "/health";
    public const string Host = "/api/host";
    public const string Apps = "/api/apps";

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
