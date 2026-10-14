using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayWiz.Models;

namespace TrayWiz.Controls;

/// <summary>Picks the bulb tile or the action tile for a home page item.</summary>
public sealed partial class TileTemplateSelector : DataTemplateSelector
{
    public DataTemplate? BulbTemplate { get; set; }

    public DataTemplate? ActionTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is LightAction ? ActionTemplate : BulbTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
