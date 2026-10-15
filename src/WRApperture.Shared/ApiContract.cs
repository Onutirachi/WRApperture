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

    /// <summary>GET: <see cref="UpdateStatusDto"/>. POST: update now (<see cref="UpdateRequestResultDto"/>).</summary>
    public const string Update = "/api/update";

    /// <summary>UDP port the agent answers discovery requests on (see <see cref="Discovery"/>).</summary>
    public const int DiscoveryPort = 5081;

    /// <summary>
    /// POST without token: ask to pair (<see cref="PairingRequestDto"/> → 202 <see cref="PairingTicketDto"/>).
    /// GET <see cref="PairingRequest"/>: its <see cref="PairingStatusDto"/>.
    /// </summary>
    public const string PairingRequests = "/pair/requests";

    public static string PairingRequest(string id) => $"{PairingRequests}/{Uri.EscapeDataString(id)}";

    /// <summary>
    /// Loopback only, with <see cref="ApprovalHeader"/>: GET <see cref="PairingDetailsDto"/>, POST
    /// <see cref="PairingDecisionDto"/>. Used by the approval window the agent opens on the host's screen.
    /// </summary>
    public static string PairingApproval(string id) => $"/pair/approval/{Uri.EscapeDataString(id)}";

    public const string ApprovalHeader = "X-WRApperture-Approval";

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
        public const string UpdateCheckFailed = "update_check_failed";

        /// <summary>Too many pairing requests from this address (or in total) right now.</summary>
        public const string PairingLimited = "pairing_limited";

        /// <summary>Nobody is signed in at the host to approve a pairing request.</summary>
        public const string NoApprover = "no_approver";
    }
}

public sealed record AppSummaryDto(string Alias, string DisplayName, string IconUrl);

/// <param name="Features">Optional capabilities (<see cref="AgentFeatures"/>); null from agents older than 0.6.0.</param>
/// <param name="Issues">What keeps remote access from working (<see cref="HostIssues"/>); null before <see cref="AgentFeatures.Readiness"/>.</param>
/// <param name="SuggestedUser">What to type as user name to sign in to the host: "PC\user" or a Microsoft account e-mail.</param>
/// <param name="MicrosoftAccount">The suggested user signs in with a Microsoft account (password, not PIN).</param>
public sealed record HostInfoDto(string HostName, int RdpPort, string AgentVersion, string CertificateFingerprint,
    IReadOnlyList<string>? Features = null, IReadOnlyList<string>? Issues = null, string? SuggestedUser = null,
    bool MicrosoftAccount = false);

/// <summary><see cref="HostInfoDto.Issues"/> codes; clients translate them (and ignore unknown ones).</summary>
public static class HostIssues
{
    public const string RdpDisabled = "rdp-disabled";
    public const string FirewallBlocked = "firewall-blocked";
    public const string PublicNetwork = "public-network";
    public const string AccountWithoutPassword = "account-without-password";
    public const string AccountNotAllowed = "account-not-allowed";
    public const string WindowsHelloOnly = "windows-hello-only";
}

public static class AgentFeatures
{
    /// <summary>The session API understands <see cref="SessionModes.Desktop"/>.</summary>
    public const string DesktopSession = "desktop-session";

    /// <summary><see cref="ApiContract.Update"/> exists: the agent can update itself from the latest release.</summary>
    public const string SelfUpdate = "self-update";

    /// <summary><see cref="HostInfoDto"/> carries Issues and SuggestedUser.</summary>
    public const string Readiness = "readiness";

    /// <summary><see cref="ApiContract.PairingRequests"/> exists: pairing approved on the host's screen, without a token.</summary>
    public const string Pairing = "pairing";
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

/// <param name="Current">The agent's version ("0.15.0").</param>
/// <param name="Latest">The latest release, null before the first successful check.</param>
/// <param name="Available">Latest is newer than Current.</param>
/// <param name="AutoUpdate">The host installs new releases by itself (HKLM\SOFTWARE\WRApperture\Agent\AutoUpdate).</param>
public sealed record UpdateStatusDto(string Current, string? Latest, bool Available, bool AutoUpdate, DateTimeOffset? CheckedAtUtc);

/// <summary>Answer to POST /api/update: 202 with <see cref="UpdateOutcomes.Installing"/>, or 200 with <see cref="UpdateOutcomes.UpToDate"/>.</summary>
public sealed record UpdateRequestResultDto(string Outcome, string Current, string? Latest);

public static class UpdateOutcomes
{
    /// <summary>The agent is downloading and verifying the release; its setup then restarts the service.</summary>
    public const string Installing = "installing";

    public const string UpToDate = "up-to-date";
}
