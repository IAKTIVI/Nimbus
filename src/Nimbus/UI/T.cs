using System.Windows.Markup;
using Nimbus.Core;

namespace Nimbus.UI;

/// <summary>Text in XAML: Text="{l:T About.Title}" shows that key in the user's language.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T(string key) : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
