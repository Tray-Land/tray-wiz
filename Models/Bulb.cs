using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayWiz.Services;
using Windows.UI;

namespace TrayWiz.Models;

public enum LightMode
{
    White,
    Color,
    Scene,
}

/// <summary>
/// One WiZ bulb. Properties are display state only and never talk to the bulb; commands are
/// sent solely through the Set* methods, which the UI calls from explicit user input. (Bound
/// controls can coerce and write back values when containers are recycled, so a property
/// setter must never be able to change a real light.)
/// </summary>
public sealed partial class Bulb : ObservableObject
{
    private LightMode _mode = LightMode.White;

    public Bulb(string mac, string ip)
    {
        Mac = mac;
        Ip = ip;
    }

    public string Mac { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string Ip { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string? Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(IsRgb), nameof(SupportsTemperature), nameof(Scenes), nameof(ColorVisibility), nameof(TemperatureVisibility))]
    public partial string ModuleName { get; set; } = "";

    [ObservableProperty]
    public partial double MinTemperature { get; set; } = 2200;

    [ObservableProperty]
    public partial double MaxTemperature { get; set; } = 6500;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial bool IsReachable { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchColor))]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial double Brightness { get; set; } = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchColor))]
    public partial double Temperature { get; set; } = 2700;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchColor))]
    public partial Color Color { get; set; } = Color.FromArgb(255, 255, 180, 100);

    /// <summary>Index into <see cref="Scenes"/>, or -1 when no scene is active.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchColor))]
    public partial int SceneIndex { get; set; } = -1;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Bulb {Mac[^4..].ToUpperInvariant()}" : Name!;

    // Module names look like ESP25_SHRGB_01 (color + white), ESP25_SHTW_01 (tunable white),
    // ESP25_SHDW_01 (dimmable only).
    public bool IsRgb => ModuleName.Contains("RGB", StringComparison.OrdinalIgnoreCase);

    public bool SupportsTemperature => IsRgb || ModuleName.Contains("TW", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<WizScene> Scenes => IsRgb ? WizScene.All : WizScene.White;

    public Microsoft.UI.Xaml.Visibility ColorVisibility => IsRgb ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility TemperatureVisibility => SupportsTemperature ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string Subtitle
    {
        get
        {
            string kind = IsRgb ? "Color" : SupportsTemperature ? "Tunable white" : ModuleName.Length > 0 ? "Dimmable" : "WiZ";
            return IsReachable ? $"{kind} · {Ip}" : $"{kind} · {Ip} · not responding";
        }
    }

    /// <summary>Approximation of what the bulb is showing, for the list swatch.</summary>
    public Color SwatchColor
    {
        get
        {
            if (!IsOn)
                return Color.FromArgb(255, 110, 110, 110);

            return _mode switch
            {
                LightMode.Color => Color,
                LightMode.Scene => Color.FromArgb(255, 180, 140, 230),
                _ => KelvinToColor(Temperature),
            };
        }
    }

    public void ApplySystemConfig(JsonObject? systemConfig, JsonObject? modelConfig)
    {
        if (systemConfig?["moduleName"]?.GetValue<string>() is string module)
            ModuleName = module;

        // cctRange is [min, ..., max]; filament bulbs report a narrow warm range.
        if (modelConfig?["cctRange"] is JsonArray range && range.Count >= 2)
        {
            double min = range[0]!.GetValue<double>();
            double max = range[^1]!.GetValue<double>();
            if (min > 0 && max > min)
            {
                MinTemperature = min;
                MaxTemperature = max;
            }
        }
    }

    public void ApplyPilot(JsonObject pilot)
    {
        IsReachable = true;
        if (pilot["state"] is JsonNode state)
            IsOn = state.GetValue<bool>();
        if (pilot["dimming"] is JsonNode dimming)
            Brightness = Math.Clamp(dimming.GetValue<double>(), 10, 100);

        int sceneId = pilot["sceneId"]?.GetValue<int>() ?? 0;
        if (pilot["temp"] is JsonNode temp && temp.GetValue<double>() > 0)
        {
            Temperature = Math.Clamp(temp.GetValue<double>(), MinTemperature, MaxTemperature);
            _mode = LightMode.White;
        }
        if (pilot["r"] is JsonNode r && pilot["g"] is JsonNode g && pilot["b"] is JsonNode b)
        {
            Color = Color.FromArgb(255, (byte)r.GetValue<int>(), (byte)g.GetValue<int>(), (byte)b.GetValue<int>());
            _mode = LightMode.Color;
        }

        // sceneId is 0 unless a scene is active (an off bulb keeps reporting its last one).
        int index = sceneId > 0 ? IndexOfScene(sceneId) : -1;
        SceneIndex = index;
        if (index >= 0)
            _mode = LightMode.Scene;

        OnPropertyChanged(nameof(SwatchColor));
    }

    public SavedBulb ToSaved() => new() { Mac = Mac, Name = Name, LastIp = Ip };

    public void SetOn(bool on)
    {
        IsOn = on;
        WizClient.SetPilotThrottled(Ip, new JsonObject { ["state"] = on });
    }

    public void SetBrightness(double value)
    {
        Brightness = Math.Clamp(Math.Round(value), 10, 100);
        SendLighting(new JsonObject { ["dimming"] = (int)Brightness }, modeChange: null);
    }

    public void SetTemperature(double kelvin)
    {
        Temperature = Math.Clamp(Math.Round(kelvin), MinTemperature, MaxTemperature);
        SendLighting(new JsonObject { ["temp"] = (int)Temperature, ["dimming"] = (int)Brightness }, LightMode.White);
    }

    public void SetColor(Color color)
    {
        if (!IsRgb)
            return;
        Color = color;
        SendLighting(new JsonObject { ["r"] = color.R, ["g"] = color.G, ["b"] = color.B, ["dimming"] = (int)Brightness }, LightMode.Color);
    }

    public void SetScene(int index)
    {
        if (index < 0 || index >= Scenes.Count)
            return;
        SceneIndex = index;
        SendLighting(new JsonObject { ["sceneId"] = Scenes[index].Id, ["dimming"] = (int)Brightness }, LightMode.Scene);
    }

    /// <summary>Blinks the bulb a few times so the user can tell which one it is, then restores it.</summary>
    [RelayCommand]
    private async Task IdentifyAsync()
    {
        JsonObject? before = await WizClient.RequestAsync(Ip, "getPilot");

        for (int i = 0; i < 3; i++)
        {
            await WizClient.SendAsync(Ip, "setPilot", new JsonObject { ["state"] = true, ["dimming"] = 100, ["temp"] = (int)MaxTemperature });
            await Task.Delay(400);
            await WizClient.SendAsync(Ip, "setPilot", new JsonObject { ["state"] = false });
            await Task.Delay(400);
        }

        await WizClient.SendAsync(Ip, "setPilot", before is null ? CurrentStateParams() : RestoreParams(before));
    }

    private void SendLighting(JsonObject parameters, LightMode? modeChange)
    {
        // Adjusting any lighting value implies the user wants the light on.
        parameters["state"] = true;
        IsOn = true;

        if (modeChange is LightMode mode)
        {
            _mode = mode;
            if (mode != LightMode.Scene)
                SceneIndex = -1;
        }
        OnPropertyChanged(nameof(SwatchColor));

        WizClient.SetPilotThrottled(Ip, parameters);
    }

    private JsonObject CurrentStateParams()
    {
        JsonObject p = new() { ["state"] = IsOn, ["dimming"] = (int)Brightness };
        switch (_mode)
        {
            case LightMode.Scene when SceneIndex >= 0:
                p["sceneId"] = Scenes[SceneIndex].Id;
                break;
            case LightMode.Color:
                p["r"] = Color.R; p["g"] = Color.G; p["b"] = Color.B;
                break;
            default:
                p["temp"] = (int)Temperature;
                break;
        }
        return p;
    }

    private static JsonObject RestoreParams(JsonObject pilot)
    {
        JsonObject p = [];
        foreach (string key in new[] { "state", "dimming", "temp", "r", "g", "b", "c", "w", "sceneId", "speed" })
        {
            if (pilot[key] is JsonNode value && !(key == "sceneId" && value.GetValue<int>() == 0))
                p[key] = value.DeepClone();
        }
        return p;
    }

    private int IndexOfScene(int sceneId)
    {
        for (int i = 0; i < Scenes.Count; i++)
        {
            if (Scenes[i].Id == sceneId)
                return i;
        }
        return -1;
    }

    // Tanner Helland's blackbody approximation, good enough for a swatch.
    private static Color KelvinToColor(double kelvin)
    {
        double t = kelvin / 100.0;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return Color.FromArgb(255, Clamp(r), Clamp(g), Clamp(b));

        static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);
    }
}
