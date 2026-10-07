using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WRApperture.Client.Core.Shortcuts;

namespace WRApperture.Client.Services;

/// <summary>
/// Icons for the shortcuts the client creates. App shortcuts carry the WRApperture badge
/// (assets/icons/badge.svg) in a corner, so they can be told apart from the same app installed locally.
/// </summary>
internal static class ShortcutIcons
{
    // Includes the sizes Windows uses at 125% / 150% / 200% scaling, so it never has to stretch a smaller one.
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 72, 96, 128, 256];

    /// <summary>The remote app's icon with the badge on its bottom-right corner, as a multi-size .ico.</summary>
    public static byte[] ForApp(byte[] appPng)
    {
        var app = Decode(appPng);
        // Small icons get a relatively bigger badge, or it would vanish at 16 px.
        return IcoWriter.FromPngs(Sizes.Select(size => Render(app, size, badge: size <= 32 ? 0.56 : 0.44)).ToList());
    }

    /// <summary>The "Desktop" art (assets/icons/desktop.svg), drawn as vectors at every size.</summary>
    public static byte[] ForDesktop() =>
        IcoWriter.FromPngs(Sizes.Select(size => Artwork.ToPng(Artwork.Desktop, size)).ToList());

    private static byte[] Render(BitmapSource image, int size, double badge)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            context.DrawImage(image, new Rect(0, 0, size, size));
            if (badge > 0)
            {
                var side = Math.Round(size * badge);
                Artwork.Draw(context, Artwork.Badge, size - side, size - side, side);
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        image.Freeze();
        return image;
    }
}
