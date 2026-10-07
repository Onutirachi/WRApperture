using System.Windows.Controls;

namespace WRApperture.Ui;

/// <summary>"Follow Windows / Light / Dark" items for any menu; the check marks follow <see cref="ThemeManager"/>.</summary>
public static class ThemeMenu
{
    /// <summary>Inserts the items at <paramref name="index"/> (default: the end).</summary>
    public static void AddTo(ItemsControl menu, int? index = null)
    {
        var items = new (ThemePreference Preference, string Header)[]
        {
            (ThemePreference.System, "Tema: seguir o Windows"),
            (ThemePreference.Light, "Tema claro"),
            (ThemePreference.Dark, "Tema escuro"),
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

        Refresh();
    }
}
