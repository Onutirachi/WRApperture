using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WRApperture.Host.Core;

namespace WRApperture.Host.Manager.ViewModels;

internal static class Icons
{
    public static ImageSource? Load(IIconProvider provider, string iconPath, int iconIndex, int size = 32)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(provider.GetPng(iconPath, iconIndex, size));
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
