using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WRApperture.Client.Core.Model;

namespace WRApperture.Client.ViewModels;

public sealed class AppTileViewModel(CachedApp app, string iconPath)
{
    public CachedApp App { get; } = app;
    public string DisplayName => App.DisplayName;
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
