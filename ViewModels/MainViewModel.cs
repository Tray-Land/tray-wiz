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

        foreach (SavedGroup saved in BulbStore.LoadGroups())
            Groups.Add(CreateGroupModel(saved.Id, saved.Name, saved.Macs));
    }

    /// <summary>Every known bulb, grouped or not.</summary>
    public ObservableCollection<Bulb> Bulbs { get; } = [];

    /// <summary>Bulbs that aren't in any group, shown above the groups.</summary>
    public ObservableCollection<Bulb> Ungrouped { get; } = [];

    public ObservableCollection<BulbGroup> Groups { get; } = [];

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

    public BulbGroup? GroupOf(Bulb bulb) => Groups.FirstOrDefault(g => g.Macs.Contains(bulb.Mac));

    /// <summary>Moves a bulb into <paramref name="target"/>, or out of its group when null.</summary>
    public void MoveToGroup(Bulb bulb, BulbGroup? target)
    {
        BulbGroup? current = GroupOf(bulb);
        if (current == target)
            return;

        if (current is null)
        {
            Ungrouped.Remove(bulb);
        }
        else
        {
            current.Macs.Remove(bulb.Mac);
            current.Bulbs.Remove(bulb);
        }

        target?.Macs.Add(bulb.Mac);
        InsertSorted(target?.Bulbs ?? Ungrouped, bulb);
        SaveGroups();
    }

    public BulbGroup CreateGroup()
    {
        string name = "New group";
        for (int n = 2; Groups.Any(g => g.Name == name); n++)
            name = $"New group {n}";

        BulbGroup group = CreateGroupModel(Guid.NewGuid().ToString("N"), name, []);
        Groups.Add(group);
        SaveGroups();
        return group;
    }

    /// <summary>Moves the group <paramref name="offset"/> places up (negative) or down in the list.</summary>
    public void MoveGroup(BulbGroup group, int offset)
    {
        int from = Groups.IndexOf(group);
        int to = from + offset;
        if (from < 0 || to < 0 || to >= Groups.Count)
            return;

        Groups.Move(from, to);
        SaveGroups();
    }

    /// <summary>Deletes the group; its bulbs go back to the ungrouped list.</summary>
    public void DeleteGroup(BulbGroup group)
    {
        foreach (Bulb bulb in group.Bulbs.ToList())
            InsertSorted(Ungrouped, bulb);

        Groups.Remove(group);
        SaveGroups();
    }

    private BulbGroup CreateGroupModel(string id, string name, IEnumerable<string> macs)
    {
        BulbGroup group = new(id, name, macs);
        group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BulbGroup.Name))
                SaveGroups();
        };
        return group;
    }

    private void SaveGroups() => BulbStore.SaveGroups(Groups.Select(g => g.ToSaved()));

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
        (GroupOf(bulb)?.Bulbs ?? Ungrouped).Add(bulb);
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    private void Bulb_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Bulb.Name) or nameof(Bulb.Ip))
            SaveAll();
    }

    private void SortBulbs()
    {
        Sort(Bulbs);
        Sort(Ungrouped);
        foreach (BulbGroup group in Groups)
            Sort(group.Bulbs);
    }

    private static void Sort(ObservableCollection<Bulb> bulbs)
    {
        List<Bulb> sorted = [.. bulbs.OrderBy(b => b.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = bulbs.IndexOf(sorted[i]);
            if (current != i)
                bulbs.Move(current, i);
        }
    }

    private static void InsertSorted(ObservableCollection<Bulb> bulbs, Bulb bulb)
    {
        int index = 0;
        while (index < bulbs.Count && StringComparer.CurrentCultureIgnoreCase.Compare(bulbs[index].DisplayName, bulb.DisplayName) <= 0)
            index++;
        bulbs.Insert(index, bulb);
    }

    private void SaveAll()
    {
        foreach (Bulb bulb in Bulbs)
            _saved[bulb.Mac] = bulb.ToSaved();
        BulbStore.Save(_saved.Values);
    }
}
