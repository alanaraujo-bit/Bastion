using System.Windows.Data;
using System.Windows.Markup;

namespace Bastion.Ui.Localization;

/// <summary>
/// XAML markup extension for localized text: <c>Text="{ui:T Home_Protect}"</c>.
/// Produces a one-way binding to <see cref="Loc.Current"/>'s indexer so the text
/// updates automatically when the language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T() { }
    public T(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Current,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
