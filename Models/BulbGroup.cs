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
        Bulbs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(EmptyVisibility));
    }

    public string Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Members by MAC, kept even for bulbs that weren't found this session.</summary>
    public HashSet<string> Macs { get; }

    /// <summary>The members that have been discovered.</summary>
    public ObservableCollection<Bulb> Bulbs { get; } = [];

    public Microsoft.UI.Xaml.Visibility EmptyVisibility =>
        Bulbs.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public void SetAllOn(bool on)
    {
        foreach (Bulb bulb in Bulbs)
            bulb.SetOn(on);
    }

    public SavedGroup ToSaved() => new() { Id = Id, Name = Name, Macs = [.. Macs.Order(StringComparer.OrdinalIgnoreCase)] };
}
