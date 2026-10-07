using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WRApperture.Host.Core.Platform.Native;

namespace WRApperture.Host.Core.Icons;

internal sealed class IconProvider(ILogger<IconProvider> logger) : IIconProvider
{
    public const int MinSize = 16;
    public const int MaxSize = 256;

    public byte[] GetPng(string iconPath, int iconIndex, int size = 48)
    {
        size = Math.Clamp(size, MinSize, MaxSize);
        try
        {
            var handle = Extract(Environment.ExpandEnvironmentVariables(iconPath ?? string.Empty), Math.Max(0, iconIndex), size);
            if (handle == IntPtr.Zero)
            {
                logger.LogDebug("No icon at {IconPath},{IconIndex}; using generic icon", iconPath, iconIndex);
                handle = StockApplicationIcon();
            }

            if (handle != IntPtr.Zero)
            {
                return ToPng(handle, size);
            }
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or OutOfMemoryException)
        {
            logger.LogWarning(ex, "Icon extraction failed for {IconPath},{IconIndex}", iconPath, iconIndex);
        }

        return Blank(size);
    }

    private static IntPtr Extract(string path, int index, int size)
    {
        if (!File.Exists(path))
        {
            return IntPtr.Zero;
        }

        var handles = new IntPtr[1];
        var ids = new uint[1];
        var count = IconNative.PrivateExtractIconsW(path, index, size, size, handles, ids, 1, 0);
        return count is 0 or IconNative.ExtractError ? IntPtr.Zero : handles[0];
    }

    private static IntPtr StockApplicationIcon()
    {
        var info = new IconNative.ShStockIconInfo { cbSize = (uint)Marshal.SizeOf<IconNative.ShStockIconInfo>() };
        return IconNative.SHGetStockIconInfo(IconNative.SiidApplication, IconNative.ShgsiIcon, ref info) == 0
            ? info.hIcon
            : IntPtr.Zero;
    }

    private static byte[] ToPng(IntPtr handle, int size)
    {
        try
        {
            using var icon = Icon.FromHandle(handle);
            using var source = icon.ToBitmap();
            using var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(target))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                g.DrawImage(source, 0, 0, size, size);
            }

            using var stream = new MemoryStream();
            target.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            IconNative.DestroyIcon(handle);
        }
    }

    private static byte[] Blank(int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
