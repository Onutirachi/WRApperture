namespace WRApperture.Client.Core.Model;

public sealed record CachedApp(string Alias, string DisplayName, string? IconETag = null);

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
    public IReadOnlyList<CachedApp> Apps { get; init; } = [];
    public DateTimeOffset? LastSyncUtc { get; init; }
}

public sealed record ClientState
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public Guid? SelectedHostId { get; init; }
    public IReadOnlyList<PairedHost> Hosts { get; init; } = [];

    public static ClientState Empty { get; } = new();
}
