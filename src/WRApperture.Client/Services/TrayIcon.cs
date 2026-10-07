using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WRApperture.Client.ViewModels;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Services;

/// <summary>
/// Notification-area icon: click shows the window; the menu opens the selected host's apps directly.
/// The menu is rebuilt each time it opens, so it always matches the current host and app list.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int MenuIconSize = 16;

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly MainViewModel _vm;
    private readonly Action _show;
    private readonly Action _exit;
    private readonly List<Image> _images = [];

    public TrayIcon(MainViewModel vm, Action show, Action exit)
    {
        _vm = vm;
        _show = show;
        _exit = exit;
        _menu.Opening += (_, _) => Rebuild();
        _icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "WRApperture",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _show();
            }
        };
        _icon.BalloonTipClicked += (_, _) => (_noticeAction ?? _show)();
        _icon.BalloonTipClosed += (_, _) => _noticeAction = null;
        Rebuild(); // an empty ContextMenuStrip may not open at all
    }

    private Action? _noticeAction;

    /// <summary>Clicking the notice runs <paramref name="onClick"/>, or shows the window when there is none.</summary>
    public void ShowNotice(string title, string text, Action? onClick = null)
    {
        _noticeAction = onClick;
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);
    }

    private void Rebuild()
    {
        _menu.Items.Clear();
        DisposeImages();

        var open = new ToolStripMenuItem(Strings.T("client.tray.open")) { Font = new Font(_menu.Font, FontStyle.Bold) };
        open.Click += (_, _) => _show();
        _menu.Items.Add(open);

        if (_vm.Hosts.Count == 1)
        {
            // One host: its apps right in the menu.
            var host = _vm.Hosts[0];
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem(host.Label) { Enabled = false });
            foreach (var tile in _vm.TilesFor(host))
            {
                _menu.Items.Add(AppItem(host, tile));
            }
        }
        else if (_vm.Hosts.Count > 1)
        {
            // Several hosts: one submenu each (the selected one in bold), so any host's app is one hover away.
            _menu.Items.Add(new ToolStripSeparator());
            foreach (var host in _vm.Hosts)
            {
                var submenu = new ToolStripMenuItem(host.Label);
                if (ReferenceEquals(host, _vm.SelectedHost))
                {
                    submenu.Font = new Font(_menu.Font, FontStyle.Bold);
                }

                foreach (var tile in _vm.TilesFor(host))
                {
                    submenu.DropDownItems.Add(AppItem(host, tile));
                }

                _menu.Items.Add(submenu);
            }
        }

        _menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem(Strings.T("client.tray.exit"));
        exit.Click += (_, _) => _exit();
        _menu.Items.Add(exit);
    }

    private ToolStripMenuItem AppItem(HostOption host, AppTileViewModel tile)
    {
        var item = new ToolStripMenuItem(tile.DisplayName, ImageFor(tile));
        item.Click += (_, _) => _vm.OpenFromTray(host, tile);
        return item;
    }

    /// <summary>The desktop art, the app's cached icon, or the grid's blue tile with the initial.</summary>
    private Image? ImageFor(AppTileViewModel tile)
    {
        var pixels = (int)Math.Round(MenuIconSize * _menu.DeviceDpi / 96.0);
        var image = tile.IsDesktop ? RenderDesktop(pixels) : LoadImage(tile.IconPath, pixels) ?? RenderTile(tile, pixels);
        if (image is not null)
        {
            _images.Add(image);
        }

        return image;
    }

    /// <summary>Read into memory: Image.FromFile would keep the icon file locked during syncs.</summary>
    private static Bitmap? LoadImage(string? path, int pixels)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var original = Image.FromStream(stream);
            var image = new Bitmap(pixels, pixels);
            using var graphics = Graphics.FromImage(image);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, 0, 0, pixels, pixels);
            return image;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The desktop item's art, drawn as vectors at menu size.</summary>
    private static Bitmap RenderDesktop(int pixels)
    {
        using var stream = new MemoryStream(Artwork.ToPng(Artwork.Desktop, pixels));
        using var decoded = Image.FromStream(stream);
        return new Bitmap(decoded);
    }

    /// <summary>Draws the grid's fallback tile with WPF (same accent color, initial and font) as a menu image.</summary>
    private static Bitmap RenderTile(AppTileViewModel tile, int pixels)
    {
        var accent = System.Windows.Application.Current.TryFindResource("Accent") as System.Windows.Media.Brush
                     ?? System.Windows.Media.Brushes.RoyalBlue;
        var glyph = new System.Windows.Media.FormattedText(tile.Initial, System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, new System.Windows.Media.Typeface("Segoe UI"), pixels * 0.6,
            System.Windows.Media.Brushes.White, 1.0);

        var visual = new System.Windows.Media.DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(accent, null, new System.Windows.Rect(0, 0, pixels, pixels), pixels / 6.0, pixels / 6.0);
            context.DrawText(glyph, new System.Windows.Point((pixels - glyph.Width) / 2, (pixels - glyph.Height) / 2));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(pixels, pixels, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var decoded = Image.FromStream(stream);
        return new Bitmap(decoded);
    }

    private void DisposeImages()
    {
        foreach (var image in _images)
        {
            image.Dispose();
        }

        _images.Clear();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        DisposeImages();
    }
}
