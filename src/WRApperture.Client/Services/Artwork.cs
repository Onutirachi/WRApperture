using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WRApperture.Client.Services;

/// <summary>
/// The author's vector art (assets/icons/*.xaml, generated from the SVGs by scripts/build-icons.ps1), each a
/// 1×1 drawing. Drawn at the exact size needed, so it stays sharp from 16 px menu items to 256 px icons.
/// </summary>
internal static class Artwork
{
    private static readonly Lazy<Drawing> BadgeDrawing = new(() => Load("badge.xaml"));
    private static readonly Lazy<Drawing> DesktopDrawing = new(() => Load("desktop.xaml"));
    private static readonly Lazy<ImageSource> DesktopSource = new(() =>
    {
        var image = new DrawingImage(Desktop);
        image.Freeze();
        return image;
    });

    /// <summary>Corner badge on shortcuts created by the client.</summary>
    public static Drawing Badge => BadgeDrawing.Value;

    /// <summary>The host's full desktop: grid tile, tray item and desktop shortcut.</summary>
    public static Drawing Desktop => DesktopDrawing.Value;

    public static ImageSource DesktopImage => DesktopSource.Value;

    /// <summary>Draws <paramref name="art"/> into the square at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void Draw(DrawingContext context, Drawing art, double x, double y, double side)
    {
        context.PushTransform(new TranslateTransform(x, y));
        context.PushTransform(new ScaleTransform(side, side));
        context.DrawDrawing(art);
        context.Pop();
        context.Pop();
    }

    /// <summary>A square PNG of <paramref name="art"/> at <paramref name="pixels"/> px.</summary>
    public static byte[] ToPng(Drawing art, int pixels)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            Draw(context, art, 0, 0, pixels);
        }

        var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Drawing Load(string name)
    {
        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}")).Stream;
        var drawing = (Drawing)System.Windows.Markup.XamlReader.Load(stream);
        drawing.Freeze();
        return drawing;
    }
}
