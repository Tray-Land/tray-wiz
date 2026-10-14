using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TrayWiz.Services;

namespace TrayWiz.Models;

/// <summary>A user-named set of bulbs shown together under a heading.</summary>
public sealed partial class BulbGroup : ObservableObject
{
    public BulbGroup(string id, string name, IEnumerable<string> macs)
    {
        Id = id;
        Name = name;
        Macs = new HashSet<string>(macs, StringComparer.OrdinalIgnoreCase);
        Tiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(EmptyVisibility));
    }

    public string Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>
    /// Members by <see cref="TileItem.Key"/> (a bulb's MAC, or an action's key), kept even for
    /// bulbs that weren't found this session. Serialized as "macs" for older files.
    /// </summary>
    public HashSet<string> Macs { get; }

    /// <summary>The members that are currently loaded.</summary>
    public ObservableCollection<TileItem> Tiles { get; } = [];

    public Microsoft.UI.Xaml.Visibility EmptyVisibility =>
        Tiles.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public void SetAllOn(bool on)
    {
        foreach (Bulb bulb in Tiles.OfType<Bulb>())
            bulb.SetOn(on);
    }

    public SavedGroup ToSaved() => new() { Id = Id, Name = Name, Macs = [.. Macs.Order(StringComparer.OrdinalIgnoreCase)] };
}
