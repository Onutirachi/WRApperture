using System.Windows;

namespace WRApperture.Ui;

/// <summary>
/// Corner radius of the themed controls (buttons, text boxes…), whose templates read it: 6 by default, set it to
/// get pills (<c>ui:Corner.Radius="16"</c>) or segmented controls ("16,0,0,16").
/// </summary>
public static class Corner
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(CornerRadius), typeof(Corner), new FrameworkPropertyMetadata(new CornerRadius(6)));

    public static CornerRadius GetRadius(DependencyObject element) => (CornerRadius)element.GetValue(RadiusProperty);

    public static void SetRadius(DependencyObject element, CornerRadius value) => element.SetValue(RadiusProperty, value);
}
