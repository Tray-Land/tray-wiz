using CommunityToolkit.Mvvm.ComponentModel;

namespace TrayWiz.Models;

/// <summary>Something that shows as a tile on the home page: a bulb or a custom action.</summary>
public abstract class TileItem : ObservableObject
{
    /// <summary>Stable identity, used to remember which group the tile lives in.</summary>
    public abstract string Key { get; }

    public abstract string DisplayName { get; }
}
