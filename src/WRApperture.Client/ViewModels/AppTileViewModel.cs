using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WRApperture.Client.Core.Model;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.ViewModels;

public sealed class AppTileViewModel(CachedApp app, string iconPath, bool isDesktop = false)
{
    /// <summary>The fixed "Desktop" tile that opens the host's full desktop.</summary>
    public static AppTileViewModel Desktop() => new(new CachedApp("@desktop", Strings.T("client.desktop.name")), string.Empty, isDesktop: true);

    public CachedApp App { get; } = app;
    public bool IsDesktop { get; } = isDesktop;
    public bool CanRename => !IsDesktop;

    /// <summary>Shown name: the local one, or the host's.</summary>
    public string DisplayName => App.Name;
    public bool IsRenamed => App.CustomName is not null;
    /// <summary>Name (and the host's name when renamed), plus a hint that the right button has more options.</summary>
    public string ToolTip => (IsDesktop ? Strings.T("client.desktop.tip")
        : IsRenamed ? Strings.T("client.tile.renamedTip", DisplayName, App.DisplayName) : DisplayName)
        + "\n" + Strings.T("client.tile.moreTip");
    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "?";
    public ImageSource? Icon { get; } = isDesktop ? Services.Artwork.DesktopImage : LoadIcon(iconPath);

    /// <summary>The cached PNG (tray menu); empty for the desktop tile.</summary>
    public string IconPath { get; } = iconPath;

    private static ImageSource? LoadIcon(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked (it is replaced on sync)
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 96;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
