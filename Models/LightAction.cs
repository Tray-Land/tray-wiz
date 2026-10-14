using CommunityToolkit.Mvvm.ComponentModel;
using TrayWiz.Services;

namespace TrayWiz.Models;

/// <summary>A user-defined tile that applies one set of settings to several lights when clicked.</summary>
public sealed partial class LightAction : TileItem
{
    public const string KeyPrefix = "action:";

    public LightAction(SavedAction saved)
    {
        Settings = saved;
        Name = saved.Name;
        Glyph = saved.Glyph;
    }

    public string Id => Settings.Id;

    public SavedAction Settings { get; private set; }

    public override string Key => KeyPrefix + Id;

    public override string DisplayName => Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Glyph { get; set; }

    /// <summary>Replaces the action's definition after the user edits it.</summary>
    public void Update(SavedAction saved)
    {
        Settings = saved;
        Name = saved.Name;
        Glyph = saved.Glyph;
    }
}
