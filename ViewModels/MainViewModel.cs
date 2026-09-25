using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayWiz.Models;
using TrayWiz.Services;

namespace TrayWiz.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public static MainViewModel Instance { get; } = new();

    private readonly Dictionary<string, SavedBulb> _saved = BulbStore.Load();

    private MainViewModel()
    {
        WizClient.CommandSuppressed += () => StatusText = $"Dry run · {WizClient.SuppressedCount} command(s) blocked";
    }

    public ObservableCollection<Bulb> Bulbs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public Microsoft.UI.Xaml.Visibility EmptyStateVisibility =>
        !IsScanning && Bulbs.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// Finds bulbs by broadcast, also probing last-known IPs directly in case the broadcast
    /// is dropped, then loads each new bulb's capabilities and state.
    /// </summary>
    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning)
            return;

        IsScanning = true;
        StatusText = "Searching for bulbs…";
        try
        {
            Dictionary<string, JsonObject> found = await WizClient.DiscoverAsync(TimeSpan.FromSeconds(2));

            IEnumerable<string> knownIps = _saved.Values.Select(s => s.LastIp)
                .Concat(Bulbs.Select(b => b.Ip))
                .OfType<string>()
                .Where(ip => !found.ContainsKey(ip))
                .Distinct();
            await Task.WhenAll(knownIps.Select(async ip =>
            {
                if (await WizClient.RequestAsync(ip, "getPilot") is JsonObject pilot)
                    lock (found) found[ip] = pilot;
            }));

            HashSet<string> seenMacs = new(StringComparer.OrdinalIgnoreCase);
            await Task.WhenAll(found.Select(async pair =>
            {
                string? mac = pair.Value["mac"]?.GetValue<string>();
                if (mac is null)
                    return;
                lock (seenMacs) seenMacs.Add(mac);
                await AddOrUpdateAsync(mac, pair.Key, pair.Value);
            }));

            foreach (Bulb missing in Bulbs.Where(b => !seenMacs.Contains(b.Mac)))
                missing.IsReachable = false;

            SortBulbs();
            SaveAll();
            StatusText = (Bulbs.Count == 1 ? "1 bulb" : $"{Bulbs.Count} bulbs") + (WizClient.DryRun ? " · dry run" : "");
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Re-reads every known bulb's state; cheap enough to run each time the popup opens.</summary>
    public async Task RefreshStatesAsync()
    {
        if (IsScanning)
            return;

        await Task.WhenAll(Bulbs.ToList().Select(async bulb =>
        {
            if (await WizClient.RequestAsync(bulb.Ip, "getPilot") is JsonObject pilot)
                bulb.ApplyPilot(pilot);
            else
                bulb.IsReachable = false;
        }));
    }

    [RelayCommand]
    private void AllOn() => SetAll(true);

    [RelayCommand]
    private void AllOff() => SetAll(false);

    private void SetAll(bool on)
    {
        foreach (Bulb bulb in Bulbs.Where(b => b.IsReachable))
            bulb.SetOn(on);
    }

    private async Task AddOrUpdateAsync(string mac, string ip, JsonObject pilot)
    {
        Bulb? existing = Bulbs.FirstOrDefault(b => string.Equals(b.Mac, mac, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Ip = ip;
            existing.ApplyPilot(pilot);
            return;
        }

        Bulb bulb = new(mac, ip);
        if (_saved.TryGetValue(mac, out SavedBulb? saved))
            bulb.Name = saved.Name;

        // Capabilities must be in place before state is applied and before the bulb reaches
        // the UI, so bound sliders/pickers never coerce a value and write it back to the bulb.
        Task<JsonObject?> system = WizClient.RequestAsync(ip, "getSystemConfig");
        Task<JsonObject?> model = WizClient.RequestAsync(ip, "getModelConfig");
        bulb.ApplySystemConfig(await system, await model);
        bulb.ApplyPilot(pilot);

        bulb.PropertyChanged += Bulb_PropertyChanged;
        Bulbs.Add(bulb);
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    private void Bulb_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Bulb.Name) or nameof(Bulb.Ip))
            SaveAll();
    }

    private void SortBulbs()
    {
        List<Bulb> sorted = [.. Bulbs.OrderBy(b => b.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = Bulbs.IndexOf(sorted[i]);
            if (current != i)
                Bulbs.Move(current, i);
        }
    }

    private void SaveAll()
    {
        foreach (Bulb bulb in Bulbs)
            _saved[bulb.Mac] = bulb.ToSaved();
        BulbStore.Save(_saved.Values);
    }
}
