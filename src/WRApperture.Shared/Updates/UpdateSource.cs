namespace WRApperture.Shared.Updates;

/// <summary>The two installers published with every release.</summary>
public enum UpdateProduct
{
    Client,
    Host,
}

/// <summary>Where releases come from and how they are installed (specs/*-github-releases, "Contrato entre as partes").</summary>
public static class UpdateSource
{
    public const string Repository = "Onutirachi/WRApperture";
    public const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>Only assets under this path of github.com are ever downloaded.</summary>
    public const string DownloadPathPrefix = "/" + Repository + "/releases/download/";

    /// <summary>Lines of "&lt;lowercase sha256 hex&gt;  &lt;file name&gt;".</summary>
    public const string ChecksumsAsset = "SHA256SUMS.txt";

    public const string SilentArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";

    /// <summary>At most one check per interval: GitHub allows 60 unauthenticated API calls per hour per IP.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>"WRApperture-Client-Setup-1.2.3.exe" / "WRApperture-Host-Setup-1.2.3.exe".</summary>
    public static string SetupAssetName(UpdateProduct product, Version version) =>
        $"WRApperture-{product}-Setup-{ProductVersion.Format(version)}.exe";

    /// <summary>Where the installer puts each product (per user for the client, Program Files for the host).</summary>
    public static string InstallDirectory(UpdateProduct product) => product == UpdateProduct.Client
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WRApperture")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WRApperture");

    /// <summary>
    /// True when this process runs from the installer's folder. Unattended updates happen only then, so a
    /// build started from a development folder never installs a second copy behind the developer's back.
    /// </summary>
    public static bool RunsFromInstallDirectory(UpdateProduct product) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(InstallDirectory(product))), StringComparison.OrdinalIgnoreCase);
}
