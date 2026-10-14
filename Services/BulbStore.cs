using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TrayWiz.Services;

public sealed class SavedBulb
{
    public string Mac { get; set; } = "";
    public string? Name { get; set; }
    public string? LastIp { get; set; }

    /// <summary>Capabilities, kept so a bulb can be shown while it is switched off at the fixture.</summary>
    public string? ModuleName { get; set; }
    public double? MinTemperature { get; set; }
    public double? MaxTemperature { get; set; }
}

public sealed class SavedGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Members by MAC, including bulbs that weren't found this session.</summary>
    public List<string> Macs { get; set; } = [];
}

/// <summary>What a custom action does to the lights' appearance. Null settings are left alone.</summary>
public enum ActionLook
{
    Unchanged,
    White,
    Color,
    Scene,
}

/// <summary>A user-defined button that applies the same settings to a set of lights.</summary>
public sealed class SavedAction
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "";

    /// <summary>Whole groups the action covers; membership is resolved when it runs.</summary>
    public List<string> GroupIds { get; set; } = [];

    /// <summary>Individually chosen bulbs, by MAC.</summary>
    public List<string> Macs { get; set; } = [];

    /// <summary>True = turn on, false = turn off, null = leave the power state alone.</summary>
    public bool? Power { get; set; }
    public int? Brightness { get; set; }
    public ActionLook Look { get; set; }
    public int? Temperature { get; set; }

    /// <summary>RRGGBB.</summary>
    public string? Color { get; set; }
    public int? SceneId { get; set; }
}

/// <summary>What a tray icon represents.</summary>
public enum TrayTargetKind
{
    Light,
    Group,
    Action,
}

/// <summary>What one kind of click on a tray icon does.</summary>
public enum TrayClickKind
{
    None,

    /// <summary>The icon's own menu. Only valid for a single left or right click.</summary>
    ShowMenu,
    OpenPopup,

    /// <summary>Lights and groups: flip power.</summary>
    Toggle,

    /// <summary>Lights and groups: step through <see cref="SavedTrayClick.Values"/> as brightness percentages.</summary>
    CycleBrightness,

    /// <summary>Lights and groups: step through <see cref="SavedTrayClick.Values"/> as RRGGBB colors.</summary>
    CycleColor,

    /// <summary>Custom buttons: run the button.</summary>
    Run,
}

public sealed class SavedTrayClick
{
    public TrayClickKind Kind { get; set; }

    /// <summary>The values a cycle swaps between, in order.</summary>
    public List<string> Values { get; set; } = [];
}

/// <summary>An extra notification-area icon bound to a light, a group, or a custom button.</summary>
public sealed class SavedTrayIcon
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "";
    public TrayTargetKind TargetKind { get; set; }

    /// <summary>A bulb's MAC, a group's id, or an action's id, depending on <see cref="TargetKind"/>.</summary>
    public string TargetId { get; set; } = "";

    public SavedTrayClick LeftClick { get; set; } = new();
    public SavedTrayClick RightClick { get; set; } = new();
    public SavedTrayClick LeftDoubleClick { get; set; } = new();
    public SavedTrayClick RightDoubleClick { get; set; } = new();
}

[JsonSerializable(typeof(List<SavedBulb>))]
[JsonSerializable(typeof(List<SavedGroup>))]
[JsonSerializable(typeof(List<SavedAction>))]
[JsonSerializable(typeof(List<SavedTrayIcon>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BulbStoreJsonContext : JsonSerializerContext;

/// <summary>
/// Persists user-given bulb names, last-known IPs (keyed by MAC address) and bulb groups.
/// Bulbs don't store names themselves (the WiZ app keeps them in its cloud), so TrayWiz keeps its own.
/// </summary>
public static class BulbStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayWiz");

    private static readonly string BulbsPath = Path.Combine(Folder, "bulbs.json");
    private static readonly string GroupsPath = Path.Combine(Folder, "groups.json");
    private static readonly string ActionsPath = Path.Combine(Folder, "actions.json");
    private static readonly string TrayIconsPath = Path.Combine(Folder, "trayicons.json");

    public static Dictionary<string, SavedBulb> Load() =>
        (Read(BulbsPath, BulbStoreJsonContext.Default.ListSavedBulb) ?? [])
            .Where(b => !string.IsNullOrEmpty(b.Mac))
            .ToDictionary(b => b.Mac, StringComparer.OrdinalIgnoreCase);

    public static void Save(IEnumerable<SavedBulb> bulbs) =>
        Write(BulbsPath, bulbs.OrderBy(b => b.Mac).ToList(), BulbStoreJsonContext.Default.ListSavedBulb);

    /// <summary>Groups in display order.</summary>
    public static List<SavedGroup> LoadGroups() =>
        [.. (Read(GroupsPath, BulbStoreJsonContext.Default.ListSavedGroup) ?? []).Where(g => !string.IsNullOrEmpty(g.Id))];

    public static void SaveGroups(IEnumerable<SavedGroup> groups) =>
        Write(GroupsPath, groups.ToList(), BulbStoreJsonContext.Default.ListSavedGroup);

    public static List<SavedAction> LoadActions() =>
        [.. (Read(ActionsPath, BulbStoreJsonContext.Default.ListSavedAction) ?? []).Where(a => !string.IsNullOrEmpty(a.Id))];

    public static void SaveActions(IEnumerable<SavedAction> actions) =>
        Write(ActionsPath, actions.ToList(), BulbStoreJsonContext.Default.ListSavedAction);

    public static List<SavedTrayIcon> LoadTrayIcons() =>
        [.. (Read(TrayIconsPath, BulbStoreJsonContext.Default.ListSavedTrayIcon) ?? []).Where(t => !string.IsNullOrEmpty(t.Id) && !string.IsNullOrEmpty(t.TargetId))];

    public static void SaveTrayIcons(IEnumerable<SavedTrayIcon> icons) =>
        Write(TrayIconsPath, icons.ToList(), BulbStoreJsonContext.Default.ListSavedTrayIcon);

    private static T? Read<T>(string path, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize(File.ReadAllText(path), typeInfo);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[BulbStore] Load of {Path.GetFileName(path)} failed: {ex.Message}");
        }

        return default;
    }

    private static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(path, JsonSerializer.Serialize(value, typeInfo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[BulbStore] Save of {Path.GetFileName(path)} failed: {ex.Message}");
        }
    }
}
