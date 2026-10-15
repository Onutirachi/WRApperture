using System.Windows.Controls;
using WRApperture.Shared.Localization;

namespace WRApperture.Ui;

/// <summary>"Follow Windows / Light / Dark" items for any menu; the check marks follow <see cref="ThemeManager"/>.</summary>
public static class ThemeMenu
{
    /// <summary>Inserts the items at <paramref name="index"/> (default: the end).</summary>
    public static void AddTo(ItemsControl menu, int? index = null)
    {
        var items = new (ThemePreference Preference, string Header)[]
        {
            (ThemePreference.System, Strings.T("common.theme.system")),
            (ThemePreference.Light, Strings.T("common.theme.light")),
            (ThemePreference.Dark, Strings.T("common.theme.dark")),
        }.Select(o =>
        {
            var item = new MenuItem { Header = o.Header, IsCheckable = true, Tag = o.Preference };
            item.Click += (_, _) => ThemeManager.SetPreference(o.Preference);
            return item;
        }).ToList();

        void Refresh()
        {
            foreach (var item in items)
            {
                item.IsChecked = (ThemePreference)item.Tag == ThemeManager.Preference;
            }
        }

        var position = index ?? menu.Items.Count;
        foreach (var item in items)
        {
            menu.Items.Insert(position++, item);
        }

        if (menu is ContextMenu context)
        {
            context.Opened += (_, _) => Refresh();
        }
        else if (menu is MenuItem submenu)
        {
            submenu.SubmenuOpened += (_, _) => Refresh();
        }

        Refresh();
    }
}
