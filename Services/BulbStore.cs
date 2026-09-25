using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrayWiz.Services;

public sealed class SavedBulb
{
    public string Mac { get; set; } = "";
    public string? Name { get; set; }
    public string? LastIp { get; set; }
}

[JsonSerializable(typeof(List<SavedBulb>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BulbStoreJsonContext : JsonSerializerContext;

/// <summary>
/// Persists user-given bulb names and last-known IPs, keyed by MAC address.
/// Bulbs don't store names themselves (the WiZ app keeps them in its cloud), so TrayWiz keeps its own.
/// </summary>
public static class BulbStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayWiz", "bulbs.json");

    public static Dictionary<string, SavedBulb> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                List<SavedBulb>? list = JsonSerializer.Deserialize(File.ReadAllText(FilePath), BulbStoreJsonContext.Default.ListSavedBulb);
                if (list is not null)
                    return list.Where(b => !string.IsNullOrEmpty(b.Mac)).ToDictionary(b => b.Mac, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[BulbStore] Load failed: {ex.Message}");
        }

        return new Dictionary<string, SavedBulb>(StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(IEnumerable<SavedBulb> bulbs)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string json = JsonSerializer.Serialize(bulbs.OrderBy(b => b.Mac).ToList(), BulbStoreJsonContext.Default.ListSavedBulb);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[BulbStore] Save failed: {ex.Message}");
        }
    }
}
