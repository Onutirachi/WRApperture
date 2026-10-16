namespace WRApperture.Host.Core.Publishing;

/// <summary>RemoteApps WRApperture publishes for itself.</summary>
public static class BuiltInApps
{
    /// <summary>The host app (WRApperture.Host.exe), opened by the clients' "Manage host"; hidden from the apps list.</summary>
    public const string ManagerAlias = "wrapperture-host";

    public const string ManagerDisplayName = "WRApperture Host";
}
