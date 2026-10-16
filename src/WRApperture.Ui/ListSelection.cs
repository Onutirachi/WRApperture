using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WRApperture.Ui;

/// <summary>
/// <c>ui:ListSelection.ClearOnEmptyClick="True"</c> on a ListBox: clicking where there is no item (nor scroll bar)
/// clears the selection, like the empty space of an Explorer folder.
/// </summary>
public static class ListSelection
{
    public static readonly DependencyProperty ClearOnEmptyClickProperty = DependencyProperty.RegisterAttached(
        "ClearOnEmptyClick", typeof(bool), typeof(ListSelection), new PropertyMetadata(false, OnChanged));

    public static bool GetClearOnEmptyClick(DependencyObject element) => (bool)element.GetValue(ClearOnEmptyClickProperty);

    public static void SetClearOnEmptyClick(DependencyObject element, bool value) => element.SetValue(ClearOnEmptyClickProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list)
        {
            return;
        }

        list.PreviewMouseLeftButtonDown -= OnMouseDown;
        if ((bool)e.NewValue)
        {
            list.PreviewMouseLeftButtonDown += OnMouseDown;
        }
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != list; element = Parent(element))
        {
            if (element is ListBoxItem or ScrollBar)
            {
                return;
            }
        }

        list.SelectedItem = null;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
