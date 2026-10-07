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
        _icon.BalloonTipClicked += (_, _) => _show();
        Rebuild(); // an empty ContextMenuStrip may not open at all
    }

    public void ShowNotice(string title, string text) => _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    private void Rebuild()
    {
        _menu.Items.Clear();
        DisposeImages();

        var open = new ToolStripMenuItem(Strings.T("client.tray.open")) { Font = new Font(_menu.Font, FontStyle.Bold) };
        open.Click += (_, _) => _show();
        _menu.Items.Add(open);

        if (_vm.SelectedHost is { } host)
        {
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem(host.Label) { Enabled = false });
            foreach (var tile in _vm.Apps)
            {
                var item = new ToolStripMenuItem(tile.DisplayName, LoadImage(tile.IconPath));
                item.Click += (_, _) => _vm.OpenAppCommand.Execute(tile);
                _menu.Items.Add(item);
            }
        }

        _menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem(Strings.T("client.tray.exit"));
        exit.Click += (_, _) => _exit();
        _menu.Items.Add(exit);
    }

    /// <summary>Read into memory: Image.FromFile would keep the icon file locked during syncs.</summary>
    private Image? LoadImage(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var original = Image.FromStream(stream);
            var image = new Bitmap(original, MenuIconSize, MenuIconSize);
            _images.Add(image);
            return image;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
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
