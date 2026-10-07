namespace WRApperture.Client.Core.Model;

/// <param name="DisplayName">Name published on the host.</param>
/// <param name="CustomName">Name chosen on this computer only (never sent to the host).</param>
public sealed record CachedApp(string Alias, string DisplayName, string? IconETag = null, string? CustomName = null)
{
    /// <summary>What the user sees: the local name, or the host's.</summary>
    public string Name => CustomName ?? DisplayName;

    /// <summary>Blank or equal to the host's name means "no local name".</summary>
    public CachedApp WithCustomName(string? name)
    {
        var clean = string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return this with { CustomName = clean.Length == 0 || clean == DisplayName ? null : clean };
    }
}

public sealed record PairedHost
{
    public const int DefaultRdpPort = 3389;

    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>As typed by the user. Single source of the RDP "full address" (session reuse).</summary>
    public required string Address { get; init; }

    public required int AgentPort { get; init; }

    /// <summary>DPAPI (CurrentUser) protected token, Base64.</summary>
    public required string ProtectedToken { get; init; }

    public required string PinnedFingerprint { get; init; }
    public int RdpPort { get; init; } = DefaultRdpPort;
    public string? RdpUserName { get; init; }

    /// <summary>
    /// Ask before logging off the host's desktop session when opening an app. Off by default: the
    /// session is released automatically (v0.3.0's "autoReleaseSession" field is intentionally ignored).
    /// </summary>
    public bool AskBeforeReleasingSession { get; init; }
    public IReadOnlyList<CachedApp> Apps { get; init; } = [];
    public DateTimeOffset? LastSyncUtc { get; init; }
}

public sealed record ClientState
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public Guid? SelectedHostId { get; init; }

    /// <summary>User declined the "open without security warning" setup; don't ask on every click.</summary>
    public bool SigningPromptDismissed { get; init; }
    public IReadOnlyList<PairedHost> Hosts { get; init; } = [];

    public static ClientState Empty { get; } = new();
}
