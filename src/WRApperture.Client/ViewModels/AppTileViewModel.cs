using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WRApperture.Client.Core.Model;

namespace WRApperture.Client.ViewModels;

public sealed class AppTileViewModel(CachedApp app, string iconPath, bool isDesktop = false)
{
    /// <summary>The fixed "Área de trabalho" tile that opens the host's full desktop.</summary>
    public static AppTileViewModel Desktop() => new(new CachedApp("@desktop", "Área de trabalho"), string.Empty, isDesktop: true);

    public CachedApp App { get; } = app;
    public bool IsDesktop { get; } = isDesktop;
    public bool CanRename => !IsDesktop;

    /// <summary>Shown name: the local one, or the host's.</summary>
    public string DisplayName => App.Name;
    public bool IsRenamed => App.CustomName is not null;
    public string ToolTip => IsDesktop ? "Abre a área de trabalho inteira do PC remoto, em tela cheia"
        : IsRenamed ? $"{DisplayName}\nNo PC remoto: {App.DisplayName}" : DisplayName;
    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "?";
    public ImageSource? Icon { get; } = LoadIcon(iconPath);

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
