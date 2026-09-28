using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using GameBoost.Core.Localization;

namespace GameBoost.App.Localization;

public class TextExtension : MarkupExtension
{
    private readonly string _key;

    public TextExtension(string key)
    {
        _key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding("Item[" + _key + "]")
        {
            Source = LocManager.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
