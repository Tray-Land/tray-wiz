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

[JsonSerializable(typeof(List<SavedBulb>))]
[JsonSerializable(typeof(List<SavedGroup>))]
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
