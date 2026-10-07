using System.Windows.Markup;
using WRApperture.Shared.Localization;

namespace WRApperture.Ui;

/// <summary>
/// Translated text in XAML: <c>xmlns:loc="clr-namespace:WRApperture.Ui;assembly=WRApperture.Ui"</c>, then
/// <c>Text="{loc:T common.theme.dark}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.T(Key);
}
